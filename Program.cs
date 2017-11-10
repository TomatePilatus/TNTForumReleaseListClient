using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;

namespace TNTForumReleaseListClient
{
    public class Program
    {
        const string requestUrl = "http://www.tntvillage.scambioetico.org/src/releaselist.php";

        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("No query text as input indicated.");

                return;
            }

            string query = string.Join(" ", args), outputFilePath = "output.csv";
            Console.WriteLine("Looking for torrents under the words \"{0}\"..", query);

            var releases = await GetAllReleasesAsync(search: query);

            using (var file = File.CreateText(outputFilePath))
                Program.PrintCSVData(file, releases);
            //Program.ShowData(releases);

            Console.WriteLine("Fetched {0} records and printed in {1}.", releases.Count(), outputFilePath);

            Console.Read();
        }

        #region Subroutines
        [Obsolete("Use GetAllReleasesAsync instead.", false)]
        public static IEnumerable<Release> GetAllReleases(string search = null, byte category = 0)
        {
            Console.Write("Fetching first page.. ");
            var sourceHtml = Program.GetHtmlDocument(search: search, category: category);
            var releases = Program.ReadTable(Program.GetTableNode(sourceHtml));
            var pages = Program.GetLastPageNumber(sourceHtml);

            Console.WriteLine("Fetching {0} pages.", pages - 1);
            if (pages > 100) throw new Exception();

            var bag = new ConcurrentBag<Release>(releases);
            Parallel.For(2, pages, (i, state) =>
            {
                var html = Program.GetHtmlDocument(search: search, category: category, page: (uint)i);

                foreach (var release in Program.ReadTable(Program.GetTableNode(html)))
                    bag.Add(release);
            });

            return bag;
        }

        public async static Task<IEnumerable<Release>> GetAllReleasesAsync(string search = null, byte category = 0)
        {
            Console.Write("Fetching first page.. ");

            var sourceHtml = await Program.GetHtmlDocumentAsync(search: search, category: category);
            var releases = Program.ReadTable(Program.GetTableNode(sourceHtml));
            var pages = Program.GetLastPageNumber(sourceHtml);

            Console.WriteLine("Fetching {0} pages.", pages - 1);

            if (pages > 100) throw new Exception();

            var bag = new ConcurrentBag<Release>(releases);

            await Task.WhenAll(Enumerable.Range(2, Convert.ToInt32(pages)).Select(i => // i from 2 to pages
                // await HTTP response and parse the document
                Program.GetHtmlDocumentAsync(search: search, category: category, page: Convert.ToUInt32(i))
                    // Then read the data and add each record to bag
                    .ContinueWith(html => Program.ReadTable(Program.GetTableNode(html.Result)).ForEach(bag.Add))
            ).ToArray());

            // Parallel.For(2, pages, (i, state) =>
            // {
            //     Program.GetHtmlDocumentAsync(search: search, category: category, page: Convert.ToUInt32(i))
            //         .ContinueWith(html => Program.ReadTable(Program.GetTableNode(html.Result)).ForEach(bag.Add));
            // });

            return bag;
        }
        #endregion

        #region Gather data
        public async static Task<HtmlDocument> GetHtmlDocumentAsync(byte category = 0, uint page = 1, string search = null)
        {
            var query = new Dictionary<string, string>(3);

            if (category != 0)
                query.Add("cat", category.ToString());
            else
                query.Add("cat", "undefined");

            query.Add("page", page.ToString());

            if (!String.IsNullOrWhiteSpace(search))
                query.Add("srcrel", WebUtility.UrlEncode(search));
            else
                query.Add("srcrel", "");

            var content = QueryHelpers.AddQueryString("", query).TrimStart('?');

            var request = WebRequest.Create(requestUrl) as HttpWebRequest;
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            //request.Headers["User-Agent"] = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; SV1; .NET CLR 1.1.43";

            using (var stream = await request.GetRequestStreamAsync())
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
                await writer.WriteAsync(content);

            var response = await request.GetResponseAsync();

            var htmlDoc = new HtmlDocument();
            using (var stream = response.GetResponseStream())
            {
                htmlDoc.Load(response.GetResponseStream());

                return htmlDoc;
            }
        }

        public static HtmlDocument GetHtmlDocument(byte category = 0, uint page = 1, string search = null)
        {
            var query = new Dictionary<string, string>(3);

            if (category != 0)
                query.Add("cat", category.ToString());
            else
                query.Add("cat", "undefined");

            query.Add("page", page.ToString());

            if (!String.IsNullOrWhiteSpace(search))
                query.Add("srcrel", WebUtility.UrlEncode(search));
            else
                query.Add("srcrel", "");

            var content = QueryHelpers.AddQueryString("", query).TrimStart('?');

            var request = WebRequest.Create(requestUrl) as HttpWebRequest;
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            //request.Headers["User-Agent"] = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; SV1; .NET CLR 1.1.43";

            using (var stream = request.GetRequestStreamAsync().Result)
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
                writer.Write(content);

            var response = request.GetResponseAsync().Result;

            var htmlDoc = new HtmlDocument();
            using (var stream = response.GetResponseStream())
            {
                htmlDoc.Load(response.GetResponseStream());

                return htmlDoc;
            }
        }

        public static HtmlNode GetTableNode(HtmlDocument html)
            => html.DocumentNode.SelectSingleNode("br[1]/div[1]/table[1]");

        public static uint GetLastPageNumber(HtmlDocument html)
        {
            var list = html.DocumentNode.SelectSingleNode("/div[2]/ul[1]");

            return Convert.ToUInt32(list.ChildNodes.First((node) => node.InnerText == "Ultima").Attributes.First((attr) => attr.Name == "p").Value);
        }
        #endregion

        #region Read data
        public static List<Release> ReadTable(HtmlNode tableNode)
        {
            var rows = tableNode.ChildNodes.Where((node) => node.Name == "tr").Skip(1);

            return rows.Select(Program.ReadRow).ToList();
        }

        private static Release ReadRow(HtmlNode tableRow)
        {
            if (tableRow == null)
                throw new ArgumentNullException(nameof(tableRow));

            var fields = tableRow.ChildNodes.Where((node) => node.Name == "td");
            var release = new Release();

            for (int i = 0; i < fields.Count(); i++)
            {
                switch (i)
                {
                    case 0:
                        // Torrent download link
                        // Format: <a href='{URL}'><img src='images/icon_bt_16x16.png' alt='Download torrent'></a>
                        release.Torrent = new Uri(fields.ElementAt(i).FirstChild.Attributes.First((attr) => attr.Name == "href").Value, UriKind.Absolute);

                        break;

                    case 1:
                        // Magnet link
                        // Format: <a href='{MAGNET}'><img src='images/icon_magnet_16x16.png' alt='Magnet link'></a>
                        release.Magnet = new Uri(fields.ElementAt(i).FirstChild.Attributes.First((attr) => attr.Name == "href").Value, UriKind.Absolute);

                        break;

                    case 2:
                        // Category
                        // Format: <a href='http://forum.tntvillage.scambioetico.org/index.php?act=allreleases&st=0&filter=&sb=1&sd=0&cat={CATEGORY}' target='_blank'><img src='http://forum.tntvillage.scambioetico.org/style_images/mkportal-636/icon{CATEGORY}.gif' height='16px'></a>
                        var url = new Uri(fields.ElementAt(i).FirstChild.Attributes.First((attr) => attr.Name == "href").Value, UriKind.Absolute);

                        release.Category = Convert.ToByte(QueryHelpers.ParseQuery(url.Query)["cat"]);

                        break;

                    case 3:
                        // Leechers number
                        // Format: {LEECHERS}
                        release.Leechers = Convert.ToUInt32(fields.ElementAt(i).InnerText);

                        break;

                    case 4:
                        // Seeders number
                        // Format: {SEEDERS}
                        release.Seeders = Convert.ToUInt32(fields.ElementAt(i).InnerText);

                        break;

                    case 5:
                        // Completed peers number
                        // Format: {COMPLETED}
                        release.Completed = Convert.ToUInt32(fields.ElementAt(i).InnerText);

                        break;

                    case 6:
                        // Release title + description page + notes
                        // Format: <a href='{DESCR_PAGE}' target='_blank'>{TITLE}</a> {NOTES}
                        var link = fields.ElementAt(i).FirstChild;

                        release.DescriptionPage = new Uri(link.Attributes.First((attr) => attr.Name == "href").Value, UriKind.Absolute);
                        release.Title = link.InnerText;
                        release.Notes = WebUtility.HtmlDecode(link.NextSibling.InnerText).Trim();

                        break;
                }
            }

            return release;
        }
        #endregion

        public static void ShowData(IEnumerable<Release> releases)
        {
            foreach (var release in releases)
                Console.WriteLine(release.Title);
        }

        public static void PrintCSVData(TextWriter stream, IEnumerable<Release> releases)
        {
            var separator = CultureInfo.CurrentUICulture.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";

            foreach (var release in releases)
                stream.WriteLine(string.Join(separator, release.Title, release.Notes, release.Seeders, release.Magnet));
        }
    }
}