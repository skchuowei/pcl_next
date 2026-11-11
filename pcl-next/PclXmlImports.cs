using System.Xml.Linq;

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
