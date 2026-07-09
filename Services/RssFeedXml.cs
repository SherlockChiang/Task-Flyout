using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Task_Flyout.Services
{
    internal static class RssFeedXml
    {
        public static bool TryLoadDocument(string xml, long maxCharactersInDocument, out XDocument? document)
        {
            document = null;
            try
            {
                using var reader = XmlReader.Create(new StringReader(xml), RssXmlSecurity.CreateReaderSettings(maxCharactersInDocument));
                document = XDocument.Load(reader);
                return document.Root != null;
            }
            catch (XmlException)
            {
                return false;
            }
        }
    }
}
