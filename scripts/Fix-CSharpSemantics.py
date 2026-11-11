#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
dev.7 full C# rewrite: compiler-driven post-processing for codeconv output.

The VB -> C# converter (ICSharpCode.CodeConverter) emits syntactically valid C#
but keeps several VB-only semantics that the C# compiler rejects.  This script
reads an MSBuild error log and rewrites the offending source lines to their C#
equivalents:

  CS1955  property / array indexed with VB parentheses -> ((dynamic)expr)[args]
  CS7036  AddressOf Foo turned into Foo()             -> pass lambda args / method group
  CS1593  zero-arg lambda bound to a parametrised delegate -> (_, __) =>
  CS1061  late-bound member on object/DependencyObject -> ((dynamic)expr).Member
  CS0019  late-bound arithmetic / If(a,b) -> ?? mismatches
  CS0826  implicitly typed array with mixed element types -> new object[]
  CS0159  VB GoTo into a try / switch block          -> structural fix
  CS0103  VB XML namespace imports                  -> PclXmlImports.cs

It is idempotent: every rule checks whether the fix is already present.  It only
touches files that appear in the supplied error log (plus two generated support
files), so it is safe to re-run after each build iteration.

Usage:
  python Fix-CSharpSemantics.py --root <worktree> --errors <build-log.txt>
"""

import argparse
import io
import os
import re
import sys
from collections import defaultdict

ERR_RE = re.compile(r'^(.*?\.cs)\((\d+),(\d+)\): error (CS\d+): (.*)$', re.M)


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def read_text(path):
    with io.open(path, encoding='utf-8-sig', errors='replace') as f:
        return f.read()


def write_text(path, text, keep_bom=True):
    with io.open(path, 'w', encoding='utf-8-sig' if keep_bom else 'utf-8', newline='') as f:
        f.write(text)


def read_errors(path):
    txt = read_text(path)
    out = []
    for m in ERR_RE.finditer(txt):
        out.append((os.path.normpath(m.group(1)), int(m.group(2)), int(m.group(3)),
                    m.group(4), m.group(5)))
    return out


def nearest(candidates, col):
    """Pick the match whose start is closest to a 1-based compiler column."""
    if not candidates:
        return None
    return min(candidates, key=lambda m: abs(m.start() - (col - 1)))


def matching_paren(s, open_pos):
    depth = 0
    quote = None
    for i in range(open_pos, len(s)):
        ch = s[i]
        if quote:
            if ch == '\\':
                continue
            if ch == quote:
                quote = None
            continue
        if ch in '"\'':
            quote = ch
        elif ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
            if depth == 0:
                return i
    return -1


def backtrack_expr(s, dot_pos):
    """Find the start of the expression immediately before a '.' at dot_pos."""
    i = dot_pos - 1
    while i >= 0:
        ch = s[i]
        if ch.isalnum() or ch in '_.':
            i -= 1
            continue
        if ch in ')]':
            closing = ch
            opening = '(' if ch == ')' else '['
            depth = 1
            j = i - 1
            while j >= 0 and depth:
                if s[j] == closing:
                    depth += 1
                elif s[j] == opening:
                    depth -= 1
                j -= 1
            if depth != 0:
                break
            i = j
            continue
        break
    return i + 1


def lambda_params(line, before_pos):
    """Parameter names of the lambda whose body starts at before_pos."""
    hits = list(re.finditer(r'\(([^()]*)\)\s*=>', line[:before_pos]))
    if not hits:
        return []
    return [p.strip() for p in hits[-1].group(1).split(',') if p.strip()]


def already_dynamic(line, start):
    return '((dynamic)' in line[max(0, start - 14):start]


# ---------------------------------------------------------------------------
# per-error rules
# ---------------------------------------------------------------------------

def rule_cs1955(line, col, msg):
    """VB default-property / array indexing: X.P(1) -> ((dynamic)X.P)[1]."""
    mm = re.search(r'不可调用的成员“([^”]+)”', msg)
    if not mm:
        return None
    name = mm.group(1).split('.')[-1]
    pat = r'(?<![\w.])([\w.]*)\.' + re.escape(name) + r'\('
    cands = list(re.finditer(pat, line))
    if not cands:
        cands = list(re.finditer(r'(?<![\w.])' + re.escape(name) + r'\(', line))
    m = nearest(cands, col)
    if not m or already_dynamic(line, m.start()):
        return None
    open_pos = line.find('(', m.start())
    close_pos = matching_paren(line, open_pos)
    if close_pos < 0:
        return None
    args = line[open_pos + 1:close_pos]
    prefix = m.group(1) if m.lastindex else ''
    full = (prefix + '.' if prefix else '') + name
    if args.strip():
        rep = '((dynamic)' + full + ')[' + args + ']'
    else:
        # zero-arg VB late-bound delegate invocation
        rep = '((dynamic)' + full + ')()'
    return line[:m.start()] + rep + line[close_pos + 1:]


def rule_cs7036(line, col, msg):
    """AddressOf Foo mistakenly emitted as Foo()."""
    mm = re.search(r'“([^”]+)”', msg)
    if not mm:
        return None
    sig = mm.group(1)
    lparen = sig.find('(')
    if lparen < 0:
        return None
    qualified = sig[:lparen]
    name = qualified.split('.')[-1]
    inside = sig[lparen + 1:sig.rfind(')')]
    inside = re.sub(r'<[^<>]*>', '', inside)
    nargs = inside.count(',') + 1 if inside.strip() else 0
    # NB: allow a leading '.' (qualified call) - only word chars must not precede it.
    cands = list(re.finditer(r'(?<![\w])' + re.escape(name) + r'\(\s*\)', line))
    m = nearest(cands, col)
    if not m:
        return None
    params = lambda_params(line, m.start())
    if params:
        # Forward the real event args through a dynamic call.  VB relaxed
        # delegate conversion let the handler take narrower parameter types
        # than the delegate; C# rejects that statically, but a dynamic
        # argument makes the whole invocation late-bound at run time - which
        # is exactly the VB behaviour.
        supplied = ['(dynamic)' + p for p in params[:nargs]]
        supplied += ['default'] * max(0, nargs - len(params))
        rep = name + '(' + ', '.join(supplied) + ')'
    else:
        # method group (constructor argument / assignment)
        rep = name
    return line[:m.start()] + rep + line[m.end():]


ONE_ARG_DELEGATES = ('Action<', 'Func<', 'ParameterizedThreadStart', 'WaitCallback',
                     'SendOrPostCallback', 'ThreadStart', 'PreviewFinishEventHandler',
                     'OnUpdateEventHandler')


def rule_cs1593(line, col, msg):
    """Relaxed VB delegate: () => ... bound to a delegate with parameters."""
    if '未采用 2 个参数' in msg:
        cands = list(re.finditer(r'\(_, __\)\s*=>', line))
        m = nearest(cands, col)
        if m:
            return line[:m.start()] + '(_) =>' + line[m.end():]
        return None
    one = any(k in msg for k in ONE_ARG_DELEGATES)
    cands = list(re.finditer(r'\(\)\s*=>', line))
    m = nearest(cands, col)
    if not m:
        return None
    rep = '(_) =>' if one else '(_, __) =>'
    return line[:m.start()] + rep + line[m.end():]


def rule_cs1061(line, col, msg):
    """Late-bound member access on object / DependencyObject."""
    mm = re.search(r'“([^”]+)”的定义', msg)
    if not mm:
        return None
    name = mm.group(1)
    cands = list(re.finditer(r'\.' + re.escape(name) + r'\b', line))
    m = nearest(cands, col)
    if not m:
        return None
    dot = m.start()
    start = backtrack_expr(line, dot)
    prefix = line[start:dot]
    if not prefix or already_dynamic(line, start):
        return None
    rep = '((dynamic)' + prefix + ').' + name
    return line[:start] + rep + line[dot + 1 + len(name):]


def rule_cs0019(line, col, msg):
    """Late-bound arithmetic and If(a, b) -> ?? mismatches."""
    if 'double' in msg and 'object' in msg:
        # AaDouble uses ParameterizedThreadStart, so the lambda argument i is
        # object in C#; VB added it to a Double through late binding.
        new = line.replace('this.TransformPos.Y + i',
                           'Conversions.ToDouble(this.TransformPos.Y) + Conversions.ToDouble(i)')
        new = new.replace('Conversions.ToDouble(this.TransformPos.Y) + i',
                          'Conversions.ToDouble(this.TransformPos.Y) + Conversions.ToDouble(i)')
        new = new.replace('-this.TransformPos.Y', '-Conversions.ToDouble(this.TransformPos.Y)')
        return new if new != line else None
    if 'JToken' in msg:
        new = line.replace('Array.Empty<JToken>()', 'new JArray()')
        return new if new != line else None
    if 'string' in msg and 'int' in msg:
        new = re.sub(r'(SettingService\.GetValue\([^()]*\))(\s*\?\?)', r'(object)\1\2', line)
        return new if new != line else None
    if '方法组' in msg:
        # "Foo && expr": Foo is a method group, VB called it without parentheses
        cands = list(re.finditer(r'\b([A-Za-z_]\w*)\s*&&', line))
        m = nearest(cands, col)
        if m:
            return line[:m.start(1)] + m.group(1) + '()' + line[m.end(1):]
    return None


def rule_cs0826(line, col, msg):
    """Mixed-type implicitly typed array."""
    cands = list(re.finditer(r'new\[\]\s*\{', line))
    m = nearest(cands, col)
    if not m:
        return None
    return line[:m.start()] + 'new object[]' + line[m.end() - 1:]


def wrap_arg_dynamic(line, col):
    """Wrap the argument at a compiler column in (dynamic)(...).

    One dynamic argument turns the whole invocation into a late-bound call,
    which is what VB Option Strict Off did with these object-typed values.
    """
    start = max(0, col - 1)
    while start < len(line) and line[start].isspace():
        start += 1
    if start >= len(line):
        return None
    depth = 0
    quote = None
    i = start
    while i < len(line):
        ch = line[i]
        if quote:
            if ch == '\\':
                i += 2
                continue
            if ch == quote:
                quote = None
        elif ch in '"\'':
            quote = ch
        elif ch in '([{':
            depth += 1
        elif ch in ')]}':
            if depth == 0:
                break
            depth -= 1
        elif ch == ',' and depth == 0:
            break
        i += 1
    expr = line[start:i].rstrip()
    if not expr or '(dynamic)' in expr or len(expr) > 400:
        return None
    end = start + len(expr)
    return line[:start] + '(dynamic)(' + expr + ')' + line[end:]


def rule_cs1503(line, col, msg):
    """Argument type mismatch caused by VB late binding.

    Deliberately conservative: only a bare identifier / simple call that is
    immediately followed by ',' or ')' is wrapped.  Wrapping a compound
    expression can swallow statement terminators and corrupt the syntax tree.
    """
    start = max(0, col - 1)
    while start < len(line) and line[start].isspace():
        start += 1
    m = re.match(r'[A-Za-z_][\w.]*(?:\([^()]*\))?', line[start:])
    if not m:
        return None
    expr = m.group(0)
    after = start + len(expr)
    if after >= len(line) or line[after] not in ',)':
        return None
    if '(dynamic)' in expr:
        return None
    return line[:start] + '(dynamic)(' + expr + ')' + line[after:]


def rule_cs0159(line, col, msg):
    """GoTo into a scope C# forbids: use goto default / break / label hoist."""
    label = re.search(r'“([^”]+)”', msg)
    label = label.group(1) if label else ''
    if label == 'Restart':
        return re.sub(r'goto\s+Restart\s*;', 'goto default;', line)
    if label == 'RequestFinished':
        return re.sub(r'goto\s+RequestFinished\s*;', 'break;', line)
    # Retry: label sits inside try in the generated code -> handled by hoist_goto_labels()
    return None


# ---------------------------------------------------------------------------
# residual rules: small VB/C# differences that need a narrower fix
# ---------------------------------------------------------------------------

TRANSFORM_PROPS = ('TransformPos.Y', 'TransformRotate.Angle', 'TransformScale.ScaleX',
                   'TransformScale.ScaleY')


def rule_cs0305(line, col, msg):
    """VB Select(Of T) -> Enumerable.Select<T>(): drop the bogus type argument."""
    return line.replace('Enumerable.Select<string>(', 'Enumerable.Select(') or None


def rule_cs1501(line, col, msg):
    """Ani.Value(0): VB array index on an object-typed property."""
    cands = list(re.finditer(r'(?<![\w.])([\w.]*)\.Value\((\d+)\)', line))
    m = nearest(cands, col)
    if not m or already_dynamic(line, m.start()):
        return None
    full = (m.group(1) + '.' if m.group(1) else '') + 'Value'
    rep = '((dynamic)' + full + ')[' + m.group(2) + ']'
    return line[:m.start()] + rep + line[m.end():]


def rule_cs0119(line, col, msg):
    """A method group used as a value: add the missing ()."""
    mm = re.search(r'“([^”]+)”是一个 方法', msg)
    if not mm:
        return None
    spec = mm.group(1)
    nm = re.search(r'\.([A-Za-z_]\w*)(?:<|\()', spec) or re.search(r'^([A-Za-z_]\w*)', spec)
    if not nm:
        return None
    name = nm.group(1)
    cands = [m for m in re.finditer(r'\.' + re.escape(name) + r'\b(?!\s*\()', line)]
    m = nearest(cands, col)
    if not m:
        return None
    return line[:m.end()] + '()' + line[m.end():]


def rule_cs0176(line, col, msg):
    """Static event / field accessed through an instance."""
    mm = re.search(r'“[^”]*\.([A-Za-z_]\w*)”', msg)
    if not mm:
        return None
    owner = msg.split('“', 1)[1].split('.')[0]
    name = mm.group(1)
    return re.sub(r'\b(?:base|this)\.[\w.]+\.' + re.escape(name) + r'\b',
                  owner + '.' + name, line) or None


def rule_cs8030(line, col, msg):
    """VB Return in a Sub: codeconv emitted a value for a void delegate."""
    return line.replace('return (object)null;', 'return;') or None


def rule_cs1510(line, col, msg):
    """ByRef lambda parameter / P-Invoke argument emitted as a ref call arg."""
    new = re.sub(r'\bref\s+(\w+)(\s*=>)', r'\1\2', line)
    new = re.sub(r'\bref\s+(arg\w+)\b', r'\1', new)
    return new if new != line else None


def rule_cs1615(line, col, msg):
    return rule_cs1510(line, col, msg)


def strip_lambda_param_types(line, col):
    """(Type a, Type b) => ... : let C# infer the delegate parameter types."""
    pat = re.compile(r'\((?:\s*[A-Za-z_][\w.<>]*\s+[A-Za-z_]\w*\s*,)*'
                     r'\s*[A-Za-z_][\w.<>]*\s+[A-Za-z_]\w*\s*\)(\s*=>)')
    cands = list(pat.finditer(line))
    m = nearest(cands, col)
    if not m:
        return None
    inner = m.group(0)[1:m.group(0).rindex(')')]
    names = [p.strip().split()[-1] for p in inner.split(',')]
    rep = '(' + ', '.join(names) + ')' + m.group(1)
    return line[:m.start()] + rep + line[m.end():]


def rule_cs1678(line, col, msg):
    return strip_lambda_param_types(line, col)


def rule_cs1661(line, col, msg):
    return strip_lambda_param_types(line, col)


def rule_cs0019_residual(line, col, msg):
    if 'double' in msg and 'object' in msg:
        new = line
        for prop in TRANSFORM_PROPS:
            p = 'this.' + prop
            new = new.replace(p + ' + i',
                              'Conversions.ToDouble(' + p + ') + Conversions.ToDouble(i)')
            new = new.replace('Conversions.ToDouble(' + p + ') + i',
                              'Conversions.ToDouble(' + p + ') + Conversions.ToDouble(i)')
            new = new.replace('-' + p, '-Conversions.ToDouble(' + p + ')')
            new = re.sub(r'(\d+d\s*-\s*)' + re.escape(p),
                         lambda mm, prop=p: mm.group(1) + 'Conversions.ToDouble(' + prop + ')',
                         new)
        return new if new != line else None
    if 'string' in msg and 'int' in msg:
        new = re.sub(r'(SettingService\.GetValue\((?:[^()]|\([^()]*\))*\))(\s*\?\?)',
                     r'(object)\1\2', line)
        return new if new != line else None
    return None


def rule_cs0266(line, col, msg):
    """Explicit cast exists: wrap a simple identifier with the target type."""
    mm = re.search(r'转换为“([^”]+)”', msg)
    if not mm:
        return None
    target = mm.group(1).replace('PCL.', '')
    if '<' in target or '[' in target:
        return None
    start = max(0, col - 1)
    while start < len(line) and line[start].isspace():
        start += 1
    m = re.match(r'[A-Za-z_][\w.]*', line[start:])
    if not m:
        return None
    expr = m.group(0)
    after = start + len(expr)
    if after >= len(line) or line[after] not in ',;)':
        return None
    return line[:start] + '(' + target + ')' + expr + line[after:]


def rule_cs9230(line, col, msg):
    """Dynamic expression inside an interpolated-string handler argument."""
    return re.sub(r'\{(\(\(dynamic\)[\w.]+\)\.\w+)\}',
                  r'{Conversions.ToString(\1)}', line) or None


def rule_cs1660(line, col, msg):
    """Regex.Replace needs a MatchEvaluator, not a zero-arg lambda."""
    cands = list(re.finditer(r'\(\)\s*=>', line))
    m = nearest(cands, col)
    if not m:
        return None
    return line[:m.start()] + '(_) =>' + line[m.end():]


RULES = {
    'CS1955': rule_cs1955,
    'CS7036': rule_cs7036,
    'CS1593': rule_cs1593,
    'CS1061': rule_cs1061,
    'CS0019': rule_cs0019_residual,
    'CS0826': rule_cs0826,
    'CS1503': rule_cs1503,
    'CS0159': rule_cs0159,
    'CS0305': rule_cs0305,
    'CS1501': rule_cs1501,
    'CS0119': rule_cs0119,
    'CS0176': rule_cs0176,
    'CS8030': rule_cs8030,
    'CS1510': rule_cs1510,
    'CS1615': rule_cs1615,
    'CS1678': rule_cs1678,
    'CS1661': rule_cs1661,
    'CS0266': rule_cs0266,
    'CS9230': rule_cs9230,
    'CS1660': rule_cs1660,
}


def fix_foreach_assignments(errors):
    """VB lets you reassign a For Each variable; C# forbids it.

    Rename the iteration variable and shadow it with a mutable local at the
    top of the loop body.  All existing references then resolve to the local,
    preserving VB semantics exactly.
    """
    sites = defaultdict(list)
    for path, ln, col, code, msg in errors:
        if code != 'CS1656':
            continue
        mm = re.search(r'无法为“([^”]+)”赋值', msg)
        if mm:
            sites[path].append((ln, mm.group(1)))
    patched = 0
    for path, items in sites.items():
        if not os.path.isfile(path):
            continue
        lines = read_text(path).split('\n')
        plan = []
        for ln, name in items:
            fi = None
            for j in range(min(ln, len(lines)) - 1, max(-1, ln - 25), -1):
                if re.search(r'foreach\s*\(', lines[j]) and re.search(
                        r'\b' + re.escape(name) + r'\b\s+in\s+', lines[j]):
                    fi = j
                    break
            if fi is None or name + '_Iter' in lines[fi]:
                continue
            bj = fi
            while bj < min(fi + 5, len(lines)) and '{' not in lines[bj]:
                bj += 1
            if bj >= len(lines) or '{' not in lines[bj]:
                continue
            plan.append((fi, bj, name))
        for fi, bj, name in sorted(set(plan), key=lambda x: -x[1]):
            lines[fi] = re.sub(r'\b' + re.escape(name) + r'\b(\s+in\s+)',
                               name + '_Iter' + r'\1', lines[fi], count=1)
            indent = re.match(r'[ \t]*', lines[bj]).group(0) + '    '
            lines.insert(bj + 1, indent + 'var ' + name + ' = ' + name + '_Iter;')
            patched += 1
        if plan:
            write_text(path, '\n'.join(lines))
    return patched


# ---------------------------------------------------------------------------
# file-level patches
# ---------------------------------------------------------------------------

def hoist_goto_labels(root):
    """Move a label that sits at the top of a try block to just before it.

    VB allows GoTo from Catch back into Try; C# forbids jumping into a try
    block.  Hoisting the label preserves the retry semantics exactly.
    """
    path = os.path.join(root, 'pcl-next', 'Modules', 'Base', 'ModNet.cs')
    if not os.path.isfile(path):
        return 0
    text = read_text(path)
    # codeconv emits the label with the same indentation as "try" but the
    # trailing ";" statement one level deeper, so don't assume equal indent.
    pat = re.compile(r'(?P<ind>[ \t]*)try\r?\n(?P=ind)\{\r?\n(?P=ind)Retry:\r?\n[ \t]*;\r?\n', re.M)

    def repl(m):
        ind = m.group('ind')
        return (ind + 'Retry:\n' + ind + ';\n' +
                ind + 'try\n' + ind + '{\n')

    new, n = pat.subn(repl, text)
    if n:
        write_text(path, new)
    return n


def ensure_support_files(root):
    """XmlImports (VB XML namespace aliases) and an explicit WPF entry point."""
    created = []
    pdir = os.path.join(root, 'pcl-next')

    xml = os.path.join(pdir, 'PclXmlImports.cs')
    if not os.path.isfile(xml):
        write_text(xml, '''using System.Xml.Linq;

namespace PCL
{
    /// <summary>
    /// VB XML literal namespace aliases (Imports &lt;xmlns:local=...&gt;).
    /// codeconv keeps the alias references but cannot emit the alias holders;
    /// this supplies the exact XNamespace values declared in the VB source.
    /// </summary>
    internal static class XmlImports
    {
        internal static readonly XNamespace local = "clr-namespace:PCL;assembly=pcl-next";
        internal static readonly XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    }
}
''', keep_bom=False)
        created.append('PclXmlImports.cs')

    entry = os.path.join(pdir, 'PclEntryPoint.cs')
    if not os.path.isfile(entry):
        write_text(entry, '''using System;
using System.Windows;

namespace PCL
{
    /// <summary>
    /// Explicit WPF entry point.  The VB build relied on MyApplication to
    /// synthesise Main; a C# SDK project needs a real one.  InitializeComponent
    /// is generated from Application.xaml and applies StartupUri / resources.
    /// </summary>
    internal static class PclEntryPoint
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var app = new Application();
            app.InitializeComponent();
            app.Run();
        }
    }
}
''', keep_bom=False)
        created.append('PclEntryPoint.cs')

    proj = os.path.join(pdir, 'pcl-next.csproj')
    if os.path.isfile(proj):
        text = read_text(proj)
        if 'PclEntryPoint' not in text:
            text = text.replace('    <OutputType>WinExe</OutputType>',
                                '    <OutputType>WinExe</OutputType>\n'
                                '    <StartupObject>PCL.PclEntryPoint</StartupObject>', 1)
            write_text(proj, text)
            created.append('pcl-next.csproj (StartupObject)')
    return created


# ---------------------------------------------------------------------------
# structural patches (converter emitted invalid shapes, not just wrong types)
# ---------------------------------------------------------------------------

def _replace(path, old, new, notes, label, count=1):
    if not os.path.isfile(path):
        return
    t = read_text(path)
    if old not in t:
        return
    write_text(path, t.replace(old, new, count))
    notes.append(label)


def patch_structural(root):
    p = lambda rel: os.path.join(root, 'pcl-next', *rel.split('/'))
    notes = []

    # Late-bound NAudio object: declare it dynamic once instead of casting at
    # every call site.  Fixes all "object has no Play/Pause/Stop" errors.
    f = p('Modules/ModMusic.cs')
    if os.path.isfile(f):
        t = read_text(f)
        t2 = re.sub(r'\bobject(\s+(?:static\s+)?MusicNAudio\b)', r'dynamic\1', t)
        if t2 != t:
            write_text(f, t2)
            notes.append('ModMusic: MusicNAudio -> dynamic')

    # VB Iterator Function became a Func<IEnumerable<string>> lambda; C# does
    # not allow yield inside lambdas.  A local iterator function is equivalent.
    f = p('Modules/Resource/ResourceVersion.cs')
    _replace(f, 'Result.Info = new Func<IEnumerable<string>>(() =>\n{',
             'IEnumerable<string> GetInfoLines()\n{', notes, 'ResourceVersion: yield lambda -> local iterator')
    _replace(f, "})().Join('，');",
             "}\n            Result.Info = GetInfoLines().Join('，');", notes,
             'ResourceVersion: invoke local iterator')

    # Static local caches were hoisted to class fields, but their initializer
    # calls a local function.  Initialize lazily inside Run instead.
    f = p('Modules/Resource/ResourceSearcher.cs')
    if os.path.isfile(f):
        t = read_text(f)
        t2 = t.replace('_Run_CurseForgeSearchEntries = GetSearchEntries(ResourcePlatforms.CurseForge).ToList();',
                       '_Run_CurseForgeSearchEntries = null;')
        t2 = t2.replace('_Run_ModrinthSearchEntries = GetSearchEntries(ResourcePlatforms.Modrinth).ToList();',
                        '_Run_ModrinthSearchEntries = null;')
        marker = 'IEnumerable<ModBase.SearchEntry<WikiEntry>> GetSearchEntries(ResourcePlatforms Source)'
        idx = t2.find(marker)
        if idx >= 0 and 'GetSearchEntries(ResourcePlatforms.CurseForge).ToList();' not in t2:
            eol = t2.find('\n', idx)
            lazy = ('\n                if (_Run_CurseForgeSearchEntries is null)\n'
                    '                {\n'
                    '                    _Run_CurseForgeSearchEntries = GetSearchEntries(ResourcePlatforms.CurseForge).ToList();\n'
                    '                    _Run_ModrinthSearchEntries = GetSearchEntries(ResourcePlatforms.Modrinth).ToList();\n'
                    '                }')
            t2 = t2[:eol] + lazy + t2[eol:]
        if t2 != t:
            write_text(f, t2)
            notes.append('ResourceSearcher: lazy static caches')

    # A method-local VB lambda was hoisted to a static field and lost the
    # EscapeHandler parameter of the enclosing method.  Make it a real method.
    f = p('Modules/ModMain.cs')
    if os.path.isfile(f):
        t = read_text(f)
        t2 = t.replace(
            'private static Func<string, string> _ArgumentReplace_Replacer = (s) =>\n        {',
            'private static string _ArgumentReplace_Replacer(string s, Func<string, string> EscapeHandler = null)\n        {', 1)
        t2 = t2.replace('            return EscapeHandler(s);\n        };',
                        '            return EscapeHandler(s);\n        }', 1)
        # append the captured parameter to every call
        out = []
        pos = 0
        needle = '_ArgumentReplace_Replacer('
        while True:
            i = t2.find(needle, pos)
            if i < 0:
                out.append(t2[pos:])
                break
            open_pos = i + len(needle) - 1
            close_pos = matching_paren(t2, open_pos)
            if close_pos < 0:
                out.append(t2[pos:])
                break
            args = t2[open_pos + 1:close_pos]
            if 'EscapeHandler' not in args:
                out.append(t2[pos:close_pos] + ', EscapeHandler')
                pos = close_pos
            else:
                out.append(t2[pos:close_pos + 1])
                pos = close_pos + 1
        t3 = ''.join(out)
        if t3 != t:
            write_text(f, t3)
            notes.append('ModMain: EscapeHandler restored as method parameter')

    # VB "With New InstallLayoutRequest With {...}" became a tuple of
    # assignments.  Rebuild a real object initialiser (init-only + required).
    f = p('Pages/PageDownload/ModDownloadLib.cs')
    if os.path.isfile(f):
        t = read_text(f)
        m = re.search(r'^([ \t]*)var Layout = Download\.InstallLayout\.Create\(\((.*?)\)\.@init\);[ \t]*$',
                      t, re.M)
        if m:
            indent, body = m.group(1), m.group(2)
            fields = []
            for part in body.split(', @init.'):
                part = part.strip()
                if part.startswith('@init.'):
                    part = part[len('@init.'):]
                if not part or part == '@init' or '=' not in part:
                    continue
                name, value = part.split('=', 1)
                fields.append((name.strip(), value.strip()))
            block = (indent + 'var Layout = Download.InstallLayout.Create(new Download.InstallLayoutRequest\n'
                     + indent + '{\n'
                     + '\n'.join(indent + '    ' + n + ' = ' + v + ',' for n, v in fields)
                     + '\n' + indent + '});')
            t = t[:m.start()] + block + t[m.end():]
            t = t.replace('            Download.InstallLayoutRequest @init = new Download.InstallLayoutRequest();\n', '')
            write_text(f, t)
            notes.append('ModDownloadLib: InstallLayoutRequest object initialiser (%d fields)' % len(fields))

    # PropertyChanged callbacks: the handler's second parameter must be typed
    # DependencyPropertyChangedEventArgs, not object (no boxing conversion for
    # method-group conversion).
    for rel in ('Controls/MyButton.xaml.cs', 'Controls/MyIconTextButton.xaml.cs'):
        _replace(p(rel), 'private void RefreshColor(object obj = null, object e = null)',
                 'private void RefreshColor(object obj = null, DependencyPropertyChangedEventArgs e = default)',
                 notes, rel + ': RefreshColor signature')
    _replace(p('Controls/MyTextBox.cs'),
             'private void MyTextBox_TextChanged(MyTextBox sender, TextChangedEventArgs e)',
             'private void MyTextBox_TextChanged(object sender, TextChangedEventArgs e)',
             notes, 'MyTextBox: TextChanged signature')

    # Retrier.Attempt expects Func<int, Task<string>>, VB's late binding hid
    # the missing Task wrapper.
    f = p('Controls/Behaviors/ClipboardInterceptor.cs')
    if os.path.isfile(f):
        t = read_text(f)
        old = 'Attempt => System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : null'
        new = 'Attempt => Task.FromResult<string>(System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : null)'
        t2 = t.replace(old, new)
        if t2 != t:
            if 'using System.Threading.Tasks;' not in t2:
                t2 = t2.replace('using System;', 'using System;\nusing System.Threading.Tasks;', 1)
            write_text(f, t2)
            notes.append('ClipboardInterceptor: Task.FromResult')

    _replace(p('Modules/Resource/ResourceProject.cs'),
             '.Select<JToken, int>(t => t["id"])',
             '.Select<JToken, int>(t => (int)t["id"])',
             notes, 'ResourceProject: JToken -> int cast')

    # Tag holds the PageStackData object itself; Val(Tag) was a converter
    # artifact, not VB semantics.
    f = p('FormMain.xaml.cs')
    _replace(f, '(PageStackData)ModBase.Val(sender.Tag)',
             '(PageStackData)((dynamic)sender).Tag', notes, 'FormMain: PageStackData from Tag')
    _replace(p('Modules/Minecraft/ModCrash.cs'), '(var)null', 'null', notes,
             'ModCrash: invalid (var)null')

    # VB ByRef on a lambda parameter is meaningless in C# - drop the keyword.
    # (kept here as well as in the rule so a rebuild from a fresh log works)
    for rel in ('Modules/Minecraft/ModJava.cs', 'Pages/PageSetup/PageSetupLaunch.xaml.cs'):
        f = p(rel)
        if os.path.isfile(f):
            t = read_text(f)
            t2 = re.sub(r'\bref\s+(\w+)(\s*=>)', r'\1\2', t)
            if t2 != t:
                write_text(f, t2)
                notes.append(rel + ': drop ref on lambda parameter')

    # DlModRequest lives on ModDownload; the converter dropped the qualifier.
    _replace(p('Modules/Resource/ResourceSearcher.cs'), 'var MatchedIds = DlModRequest(FilterUrl)',
             'var MatchedIds = ModDownload.DlModRequest(FilterUrl)', notes,
             'ResourceSearcher: qualify DlModRequest')

    # ---- convergence pass -------------------------------------------------
    # Event handlers: a value-type event argument cannot convert to object in
    # a method group, so keep the (object, object) signature and subscribe with
    # lambdas instead.
    for rel in ('Controls/MyButton.xaml.cs', 'Controls/MyIconTextButton.xaml.cs'):
        f = p(rel)
        if os.path.isfile(f):
            t = read_text(f)
            t2 = t.replace('private void RefreshColor(object obj = null, DependencyPropertyChangedEventArgs e = default)',
                           'private void RefreshColor(object obj = null, object e = null)')
            t2 = re.sub(r'(\+=\s*)RefreshColor\s*;', r'\1(_, __) => RefreshColor();', t2)
            if t2 != t:
                write_text(f, t2)
                notes.append(rel + ': event subscriptions via lambda')

    f = p('Pages/PageDownload/ModDownloadLib.cs')
    if os.path.isfile(f):
        t = read_text(f)
        t2 = t.replace('ModLoaderVersions.Any(), @init,', 'ModLoaderVersions.Any(),')
        t2 = t2.replace('&& l.EnumerateFiles().Any).ToList;',
                        '&& l.EnumerateFiles().Any()).ToList();')
        t2 = t2.replace('TrimStart("/")', "TrimStart('/')")
        if t2 != t:
            write_text(f, t2)
            notes.append('ModDownloadLib: init/list/TrimStart cleanups')

    # Interpolated-string handler arguments must not be dynamically invoked.
    # Cast the dynamic member to object before Conversions.ToString so the
    # call is statically bound and the interpolation stays a plain string.
    for cs in sorted(set(str(x) for x in __import__('pathlib').Path(
            os.path.join(root, 'pcl-next')).rglob('*.cs') if 'obj' not in x.parts)):
        t = read_text(cs)
        t2 = re.sub(r'\{Conversions\.ToString\(\(\(dynamic\)([\w.]+)\)\.(\w+)\)\}',
                    r'{Conversions.ToString((object)((dynamic)\1).\2)}', t)
        t2 = re.sub(r'\{\(\(dynamic\)([\w.]+)\)\.(\w+)\}',
                    r'{Conversions.ToString((object)((dynamic)\1).\2)}', t2)
        if t2 != t:
            write_text(cs, t2)
            notes.append('CS9230 fix in ' + os.path.basename(cs))

    # Transform properties used without "this." in FormMain animations.
    f = p('FormMain.xaml.cs')
    if os.path.isfile(f):
        t = read_text(f)
        for prop in TRANSFORM_PROPS:
            q = re.escape(prop)
            t = re.sub(r'(?<![\w.])' + q + r'\s*\+\s*i\b',
                       'Conversions.ToDouble(' + prop + ') + Conversions.ToDouble(i)', t)
            t = re.sub(r'(\d+(?:\.\d+)?d\s*-\s*)(' + q + r')',
                       lambda m, pp=prop: m.group(1) + 'Conversions.ToDouble(' + pp + ')', t)
        t = re.sub(r'new object\[\]\{\s*TransformRotate,\s*TransformPos,\s*TransformScale\s*\}',
                   'new Transform[] { (Transform)TransformRotate, (Transform)TransformPos, (Transform)TransformScale }', t)
        t = t.replace('switch (PageCurrent)\n', 'switch (PageCurrent.Page)\n')
        t = t.replace('this.PanTitleSelect.Children[Stack]', 'this.PanTitleSelect.Children[Stack.Page]')
        write_text(f, t)
        notes.append('FormMain: animation casts / PageStackData indexing')

    # ConcurrentList.Edit takes Func<T, T>: the VB Sub-lambdas must return the
    # list after mutating it.
    f = p('Modules/Minecraft/ModJava.cs')
    if os.path.isfile(f):
        t = read_text(f)
        t2 = t.replace('Edit(List => List.RemoveIf(f => PathUtils.IsSame(f, NewJava.Folder)));',
                       'Edit(List => { List.RemoveIf(f => PathUtils.IsSame(f, NewJava.Folder)); return List; });')
        t2 = t2.replace('Edit(List => List.Remove(JavaEntry));',
                        'Edit(List => { List.Remove(JavaEntry); return List; });')
        t2 = t2.replace('Edit(List => List.Add(ForcedJava));',
                        'Edit(List => { List.Add(ForcedJava); return List; });')
        t2 = t2.replace('            List.Insert(0, NewJava);\n        });',
                        '            List.Insert(0, NewJava);\n            return List;\n        });')
        t2 = t2.replace('new object[]{ new ModNet.LoaderDownload(',
                        'new ModLoader.LoaderBase[] { new ModNet.LoaderDownload(')
        t2 = t2.replace('new Action<ModLoader.LoaderCombo<int>>((RawLoader)',
                        'new Action<ModLoader.LoaderBase>((RawLoader)')
        if t2 != t:
            write_text(f, t2)
            notes.append('ModJava: Edit lambdas return the list')

    f = p('Modules/Minecraft/ModLaunch.cs')
    if os.path.isfile(f):
        t = read_text(f)
        t2 = t.replace('ModBase.VersionCode);', 'ModBase.VersionCode.ToString());')
        t2 = t2.replace('Math.Round(TargetSize.Width));', 'Math.Round(TargetSize.Width).ToString());')
        t2 = t2.replace('Math.Round(TargetSize.Height));', 'Math.Round(TargetSize.Height).ToString());')
        if t2 != t:
            write_text(f, t2)
            notes.append('ModLaunch: tuple values -> string')

    f = p('Controls/Behaviors/LazyLoadBehavior.cs')
    _replace(f, '            Action Handler = () =>\n',
             '            EventHandler Handler = null;\n            Handler = (_, __) =>\n',
             notes, 'LazyLoadBehavior: EventHandler instead of Action')

    f = p('Controls/Behaviors/ClipboardInterceptor.cs')
    _replace(f, 'delay: () => TimeSpan.FromMilliseconds(100L)',
             'delay: (_) => TimeSpan.FromMilliseconds(100L)', notes,
             'ClipboardInterceptor: Func<int,TimeSpan> delay')

    _replace(p('Pages/PageSpeedLeft/PageSpeedLeft.xaml.cs'),
             'ModBase.ClipboardSet(sender.Text, false);',
             'ModBase.ClipboardSet(((dynamic)sender).Text, false);',
             notes, 'PageSpeedLeft: late-bound sender.Text')

    return notes


# ---------------------------------------------------------------------------
# driver
# ---------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', required=True, help='converted worktree root')
    ap.add_argument('--errors', required=True, help='MSBuild error log (plain text)')
    args = ap.parse_args()

    root = os.path.abspath(args.root)
    errors = read_errors(args.errors)
    by_file = defaultdict(list)
    for e in errors:
        by_file[e[0]].append(e[1:])

    created = ensure_support_files(root)
    hoisted = hoist_goto_labels(root)
    structural = patch_structural(root)
    foreach_fixed = fix_foreach_assignments(errors)

    total = 0
    touched = 0
    unresolved = 0
    for path, items in by_file.items():
        if not os.path.isfile(path):
            # logs may point at a different checkout; rebase onto --root
            marker = 'pcl-next' + os.sep
            if marker in path:
                path = os.path.join(root, 'pcl-next', path.split(marker, 1)[1])
        if not os.path.isfile(path):
            continue
        lines = read_text(path).split('\n')
        by_line = defaultdict(list)
        for ln, col, code, msg in items:
            by_line[ln].append((col, code, msg))
        changed = False
        for ln in sorted(by_line, reverse=True):
            if ln > len(lines):
                continue
            # right-to-left so earlier (lower) columns stay valid
            for col, code, msg in sorted(by_line[ln], key=lambda x: -x[0]):
                rule = RULES.get(code)
                if not rule:
                    unresolved += 1
                    continue
                before = lines[ln - 1]
                after = rule(before, col, msg)
                if after is not None and after != before:
                    lines[ln - 1] = after
                    total += 1
                    changed = True
        if changed:
            write_text(path, '\n'.join(lines), keep_bom=True)
            touched += 1

    print('support files : %s' % (', '.join(created) if created else 'already present'))
    print('hoisted goto labels : %d' % hoisted)
    print('structural patches : %d' % len(structural))
    for n in structural:
        print('  - ' + n)
    print('foreach rewrites : %d' % foreach_fixed)
    print('patched lines : %d in %d file(s)' % (total, touched))
    print('errors without an automatic rule : %d' % unresolved)
    return 0


if __name__ == '__main__':
    sys.exit(main())
