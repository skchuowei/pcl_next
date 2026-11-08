#!/usr/bin/env python3
"""Post-process codeconv output for pcl-next (dev.7 full C# rewrite).

codeconv 10.0.1.923 cannot convert two VB constructs in this repository:

1. ``On Error Resume Next`` -> emits ``#error`` and a comment block, but the
   statements after it ARE converted. We wrap the remaining method body in
   try/catch to keep the "ignore errors and carry on" intent.
2. Some ``Dim`` declarations hit a NullReferenceException inside
   CommonConversions.ShouldPreferExplicitType; the whole statement is dropped.
   We restore each one from the VB source (see comments below).

Also cleans VB-only leftovers that survived the .vbproj -> .csproj conversion.

Usage:
    python scripts/Fix-CodeConvIssues.py --root D:/bing/MC/pcl_x/.vb2cs-trial

The script is idempotent: it only touches files that still contain #error or
the known VB-only markers. All comments are ASCII so PowerShell 5.1 is happy.
"""
import argparse
import re
import sys
from pathlib import Path

# Missing declarations, keyed by VB variable name. Values are C# statements.
# Paths are relative to <root>/pcl-next.
DECL_FIXES = {
    "ProjectList": 'var ProjectList = new List<ResourceProject>();',
    "Project": 'var Project = new ResourceProject(JsonEntry);',
    "ProjectIds": 'string ProjectIds = ProjectList.Select(p => p.Id).Join("\\",\\"project_id:");',
    "Loaders": 'string Loaders = Request.ModLoaders.Flags.Select(f => f.ToString().Lower()).Join("\\",\\"categories:");',
    "MatchedIds": 'var MatchedIds = DlModRequest(FilterUrl)["hits"].Select(h => h["project_id"].ToString()).ToList();',
    "OldList": 'var OldList = DirectoryUtils.EnumerateDirectories(McFolder + @"versions\\").ToList();',
    "RequiredJavaMajor": 'int? RequiredJavaMajor = null;',
    "DeltaList": 'var DeltaList = DirectoryUtils.EnumerateDirectories(McFolder + @"versions\\").SkipWhile(i => OldList.Contains(i)).Select(f => DirectoryUtils.GetInfo(f)).ToList();',
    "JsonFile": 'FileInfo JsonFile = DeltaList[0].EnumerateFiles.First();',
}

ONERROR_NOTE = '// [VB Port] On Error Resume Next -> ignore exceptions in the rest of this method'
ENDTRY_NOTE = '// [VB Port] end try'
DECL_NOTE = '// [VB Port] restore Dim dropped by codeconv (CommonConversions NRE)'


def find_blocks(lines):
    """Return (start, end_exclusive, kind, var_name) for every #error block."""
    out = []
    i = 0
    while i < len(lines):
        if lines[i].lstrip().startswith('#error Cannot convert'):
            j = i + 1
            while j < len(lines) and '/*' not in lines[j]:
                j += 1
            k = j
            while k < len(lines) and '*/' not in lines[k]:
                k += 1
            end = k + 1
            text = '\n'.join(lines[i:end])
            kind = 'OnError' if 'OnErrorResumeNext' in text else 'LocalDecl'
            m = re.search(r'Dim\s+(\w+)', text)
            var = m.group(1) if m else None
            out.append((i, end, kind, var))
            i = end
        else:
            i += 1
    return out


def guess_body_indent(lines, idx, fallback):
    """Indent of the first real code line after `idx` (skip converter noise).

    codeconv writes #error directives at column 0, so using the directive's own
    indent would leave the inserted code flush-left. The statements right after
    the block are already correctly indented, so reuse their leading whitespace.
    """
    for k in range(idx, min(idx + 8, len(lines))):
        s = lines[k]
        st = s.strip()
        if not st or st == ';' or st.startswith('#error') or st.startswith('/*') or st.startswith('*/'):
            continue
        return re.match(r'\s*', s).group(0)
    return fallback


def find_method_close(lines, start):
    """Find the line index of the '}' that closes the method containing `start`.

    Brace counting starts at 0 at `start`; the first '}' seen while depth is 0
    is the method's closing brace. Strings, verbatim strings, chars and comments
    are skipped so braces inside them do not skew the count.
    """
    depth = 0
    state = 'normal'  # normal | string | verbatim | char | block_comment
    for ln in range(start, len(lines)):
        line = lines[ln]
        # Braces inserted by an earlier (later-in-file) OnError patch must not
        # be counted: this function is called while walking blocks backwards.
        if ENDTRY_NOTE in line:
            continue
        col = 0
        while col < len(line):
            c = line[col]
            nxt = line[col + 1] if col + 1 < len(line) else ''
            if state == 'normal':
                if c == '/' and nxt == '/':
                    break  # rest of line is a comment
                if c == '/' and nxt == '*':
                    state = 'block_comment'
                    col += 2
                    continue
                if c == '@' and nxt == '"':
                    state = 'verbatim'
                    col += 2
                    continue
                if c == '$' and nxt == '"':
                    state = 'string'
                    col += 2
                    continue
                if c == '"':
                    state = 'string'
                    col += 1
                    continue
                if c == "'":
                    state = 'char'
                    col += 1
                    continue
                if c == '{':
                    depth += 1
                elif c == '}':
                    if depth == 0:
                        return ln
                    depth -= 1
                col += 1
                continue
            if state == 'block_comment':
                if c == '*' and nxt == '/':
                    state = 'normal'
                    col += 2
                else:
                    col += 1
                continue
            if state == 'verbatim':
                if c == '"':
                    if nxt == '"':
                        col += 2
                        continue
                    state = 'normal'
                col += 1
                continue
            if state == 'char':
                if c == '\\':
                    col += 2
                    continue
                if c == "'":
                    state = 'normal'
                col += 1
                continue
            if state == 'string':
                if c == '\\':
                    col += 2
                    continue
                if c == '"':
                    state = 'normal'
                col += 1
                continue
            col += 1
    return None


def fix_cs_file(path):
    text = path.read_text(encoding='utf-8-sig')
    lines = text.split('\n')
    blocks = find_blocks(lines)
    if not blocks:
        return 0
    # Work backwards so insertions never shift the indices of earlier blocks.
    for start, end, kind, var in reversed(blocks):
        indent = guess_body_indent(lines, end, '        ')
        if kind == 'LocalDecl':
            if var not in DECL_FIXES:
                print('  SKIP unknown Dim: %s at %s:%d' % (var, path.name, start + 1))
                continue
            replacement = [indent + DECL_NOTE, indent + DECL_FIXES[var]]
            lines[start:end] = replacement
            continue
        # OnError: insert the catch BEFORE the method's closing brace, then
        # replace the #error block with the opening try.
        close_line = find_method_close(lines, end)
        if close_line is None:
            print('  SKIP OnError (no closing brace found): %s:%d' % (path.name, start + 1))
            continue
        close_indent = re.match(r'\s*', lines[close_line]).group(0)
        lines[close_line:close_line] = [
            indent + '} ' + ENDTRY_NOTE,
            indent + 'catch (Exception) { }',
        ]
        lines[start:end] = [indent + ONERROR_NOTE, indent + 'try', indent + '{']
    path.write_text('\n'.join(lines), encoding='utf-8-sig')
    return len(blocks)


def patch_project_and_xaml(root):
    """Remove VB-only leftovers codeconv copied into the generated .csproj."""
    proj = root / 'pcl-next' / 'pcl-next.csproj'
    if proj.exists():
        t = proj.read_text(encoding='utf-8-sig')
        before = t
        # VB-specific option properties and warning codes are meaningless in C#.
        for tag in ('MyType', 'OptionExplicit', 'OptionCompare', 'OptionStrict', 'OptionInfer'):
            t = re.sub(r'\s*<%s>[^<]*</%s>' % (tag, tag), '', t)
        # Explicit framework reference / VB Imports: SDK already references it,
        # and the hand-written Reference fails to resolve (MSB3245).
        t = re.sub(r'\s*<Reference Include="Microsoft\.VisualBasic"\s*/>', '', t)
        t = re.sub(r'\s*<Import Include="[^"]*"\s*/>', '', t)
        t = re.sub(r'BC\d+(?:,BC\d+)*', '', t)
        t = t.replace('obj\\PclBuildInfo.g.vb', 'obj\\PclBuildInfo.g.cs')
        if t != before:
            proj.write_text(t, encoding='utf-8-sig')
            print('  patched %s' % proj.name)

    # x:Class="Application" needs a partial code-behind class.
    app = root / 'pcl-next' / 'Application.xaml.cs'
    if app.exists():
        t = app.read_text(encoding='utf-8-sig')
        t2 = t.replace('public class Application', 'public partial class Application', 1)
        if t2 != t:
            app.write_text(t2, encoding='utf-8-sig')
            print('  patched Application.xaml.cs (partial)')

    # x:FieldModifier is case-sensitive in C# (VB accepted "Public").
    # x:Class must be fully qualified: VB implicitly applies RootNamespace=PCL,
    # C# does not. Without the prefix the XAML-generated partial class lands in
    # the global namespace and never merges with the PCL.* code-behind class,
    # which breaks base classes, InitializeComponent and override resolution.
    xclass_count = 0
    for xaml in (root / 'pcl-next').rglob('*.xaml'):
        if 'obj' in xaml.parts or 'bin' in xaml.parts:
            continue
        t = xaml.read_text(encoding='utf-8-sig')
        t2 = t.replace('x:FieldModifier="Public"', 'x:FieldModifier="public"')

        def qualify(m):
            name = m.group(2)
            if '.' in name:
                return m.group(0)
            return m.group(1) + 'PCL.' + name + m.group(3)

        t2 = re.sub(r'(x:Class=")([^"]+)(")', qualify, t2)
        if t2 != t:
            xaml.write_text(t2, encoding='utf-8-sig')
            xclass_count += 1
    if xclass_count:
        print('  patched x:Class / x:FieldModifier in %d XAML file(s)' % xclass_count)


def patch_cs_semantics(root):
    """VB-only semantics that are legal in VB but rejected by the C# compiler."""
    pcl = root / 'pcl-next'

    # 1) VB WithEvents fields turn into properties with event hookup. In the
    # derived page the base member is an XAML-generated *field*, so C# cannot
    # override it. Hiding it with `new` keeps the hookup behaviour intact.
    mod = pcl / 'Pages' / 'PageDownload' / 'PageDownloadModify.cs'
    if mod.exists():
        t = mod.read_text(encoding='utf-8-sig')
        # All 11 members (MyIconButton / MyCard / MyTextBox / Label ...) shadow
        # XAML-generated fields of the same name in the base page.
        t2 = re.sub(r'internal override (\w+)\s', r'internal new \1 ', t)
        if t2 != t:
            mod.write_text(t2, encoding='utf-8-sig')
            print('  patched PageDownloadModify.cs (override -> new)')

    # 2) VB allows `Optional x As SomeEnum = -1`; C# needs an explicit cast.
    enum_re = re.compile(r'\b(FormMain\.PageSubType|PageSubType)\s+(\w+)\s*=\s*-1(?=\s*[,)])')
    enum_count = 0
    # 3) VB allows `Optional x As Object = ""`; C# only permits null for an
    # object default. Split into an overload so the "" default is preserved.
    obj_old = 'public static object GetProgramArgument(string Name, object DefaultValue = "")'
    obj_count = 0
    for cs in pcl.rglob('*.cs'):
        if 'obj' in cs.parts or 'bin' in cs.parts:
            continue
        t = cs.read_text(encoding='utf-8-sig')
        t2 = enum_re.sub(lambda m: '%s %s = (%s)(-1)' % (m.group(1), m.group(2), m.group(1)), t)
        if obj_old in t2:
            leading = re.search(r'(?m)^(\s*)' + re.escape(obj_old), t2)
            pad = leading.group(1) if leading else '        '
            replacement = (
                'public static object GetProgramArgument(string Name) { return GetProgramArgument(Name, ""); }\n'
                + pad + 'public static object GetProgramArgument(string Name, object DefaultValue)'
            )
            t2 = t2.replace(obj_old, replacement)
            obj_count += 1
        if t2 != t:
            cs.write_text(t2, encoding='utf-8-sig')
            enum_count += 1
    if enum_count:
        print('  patched %d file(s): enum default cast / object default overload' % enum_count)
    if obj_count:
        print('  patched GetProgramArgument default via overload')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', required=True, help='worktree root containing pcl-next/')
    args = ap.parse_args()
    root = Path(args.root)
    pcl = root / 'pcl-next'
    if not pcl.is_dir():
        print('ERROR: not found: %s' % pcl)
        return 2
    total = 0
    files = 0
    for cs in pcl.rglob('*.cs'):
        if 'obj' in cs.parts or 'bin' in cs.parts:
            continue
        n = fix_cs_file(cs)
        if n:
            files += 1
            total += n
            print('  fixed %d block(s) in %s' % (n, cs.relative_to(pcl)))
    patch_project_and_xaml(root)
    patch_cs_semantics(root)
    print('DONE: %d #error block(s) in %d file(s)' % (total, files))
    return 0


if __name__ == '__main__':
    sys.exit(main())
