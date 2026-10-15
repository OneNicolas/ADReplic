using System.Globalization;
using System.Text;

namespace ADReplic.App.ViewModels
{
    /// <summary>
    /// Emballe un fragment HTML au format CF_HTML attendu par le presse-papiers
    /// Windows : un en-tête texte avec les offsets (en octets UTF-8) du document
    /// et du fragment. Sans cet en-tête, Outlook et Word ignorent le contenu HTML.
    /// </summary>
    public static class HtmlClipboardFormat
    {
        private const string HeaderTemplate =
            "Version:0.9\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n";

        private const string DocumentPrefix  = "<html><body>\r\n<!--StartFragment-->";
        private const string DocumentSuffix  = "<!--EndFragment-->\r\n</body></html>";

        public static string Wrap(string fragment)
        {
            if (fragment == null) fragment = "";

            // Les offsets sont en octets : on mesure avec l'encodage réel du presse-papiers (UTF-8).
            var headerLength   = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, HeaderTemplate, 0, 0, 0, 0));
            var startHtml      = headerLength;
            var startFragment  = startHtml + Encoding.UTF8.GetByteCount(DocumentPrefix);
            var endFragment    = startFragment + Encoding.UTF8.GetByteCount(fragment);
            var endHtml        = endFragment + Encoding.UTF8.GetByteCount(DocumentSuffix);

            var header = string.Format(CultureInfo.InvariantCulture, HeaderTemplate, startHtml, endHtml, startFragment, endFragment);
            return header + DocumentPrefix + fragment + DocumentSuffix;
        }
    }
}
