using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace TMWImporter
{
    /*
    
        This code is one-time-use throwaway code. 
        Please don't judge :(

    */
    public class Program
    {
        private const string Repo = "tmw-issue-import/TellMeWhen";
        private const string GHToken = "";

        private static string GetDate(HtmlNode node, string def = null) {
            if (node == null) return def;
            return DateTimeOffset.FromUnixTimeSeconds(
                node.QuerySelector("[data-epoch]")
                    .GetAttributeValue("data-epoch", 0)
                ).ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'");
        }

        public static void Main(string[] args)
        {
            var client = new HttpClient();
            Environment.CurrentDirectory = "B:/Addons/TellMeWhen/CurseForge dump";

            // Parallel.For(1, 1655+1, i => 
            //Parallel.For(11, 20, i => 

            //for (int i = 1296; i <= 1656; i++)
            for (int i = 1; i <= 1656; i++)
            {
                var fileName = $"html/{i}.html";
                // if (!File.Exists(fileName)) return;
                if (!File.Exists(fileName)) continue;

                var content = File.ReadAllText(fileName);

                var doc = new HtmlDocument();
                doc.LoadHtml(content);
                var issueRoot = doc.DocumentNode
                    .QuerySelector(".project-issue-details");

                foreach (var link in issueRoot
                    .QuerySelectorAll("a"))
                {
                    var href = link.GetAttributeValue("href", "");
                    if (href.StartsWith("/linkout")){
                        href = "https://wow.curseforge.com" + href;
                        var uri = new Uri(href, UriKind.Absolute);
                        var values = HttpUtility.ParseQueryString(uri.Query);
                        href = values["remoteUrl"];
                        href = HttpUtility.UrlDecode(href);
                        link.SetAttributeValue("href", href);
                    } else {
                        var match = Regex.Match(href, @"tellmewhen/(?:tickets|issues)/(\d+)");
                        // Replace an issue link to the search results for that issue
                        // (since we don't know what the github issue will be, the best we can do is search.)
                        if (match.Success) {
                            link.SetAttributeValue(
                                "href", 
                                $"https://github.com/{Repo}/issues?q=%5BCF%20{match.Groups[1].Value}%5D+in%3Atitle");
                        }
                    }
                }

                foreach (var textNode in issueRoot
                    .DescendantsAndSelf()
                    .Where(t => t.NodeType == HtmlNodeType.Text))
                {
                    var parent = textNode;
                    while (parent != null) {
                        if (parent.Name == "pre"){
                            goto next;
                        }
                        parent = parent.ParentNode;
                    }

                    textNode.InnerHtml = 
                        Regex.Replace(
                            textNode.InnerHtml, 
                            @"( |\t|\r?\n){2,}", "$1");
                    
                    next:
                        continue;
                }

                var comments = issueRoot
                    .QuerySelectorAll(".project-issue-comment")
                    .Select(c => {
                        var header = c.QuerySelector(".project-issue-comment-header");
                        var creatorEl = header.QuerySelector("> .user-tag > a, > a");
                        var creator = creatorEl.InnerText;
                        
                        var creatorLink = 
                            "https://wow.curseforge.com" + creatorEl.GetAttributeValue("href", "");

                        var date = GetDate(header);

                        var bodyEl = c.QuerySelector(".project-issue-comment-body");

                        foreach (var pre in bodyEl.QuerySelectorAll("pre")) {
                            // e.g. #1266
                            // turn <pre> sections into markdown code blocks
                            pre.InnerHtml = "\n\n```\n" + pre.InnerText + "\n```\n";
                            pre.Name = "div";
                        }

                        var body = bodyEl.InnerHtml
                            .Replace("&#x27;", "'")
                            .Replace("&quot;", "\"");

                        body = $"{body}\n<br>\n\n> Posted by CurseForge user <a href=\"{creatorLink}\">{creator}</a>";

                        if (header.InnerText.Contains("attachment")) {
                            var link = header.QuerySelector("> a");
                            body = "<strong>Attachment Added:</strong>" + link.OuterHtml + "<br>" + body;
                        }

                        return new {
                            creator,
                            creatorLink,
                            date,
                            body
                        };
                    })
                    .ToList();
                
                var firstComment = comments.First();
                var closed = issueRoot.HasClass("closed")
                    || issueRoot.QuerySelector(".project-issue-header .project-issue-status.status-closed") != null;
                var updatedDate = issueRoot
                        .QuerySelectorAll(".standard-date")
                        .Select(e => GetDate(e))
                        .OrderBy(d => d)
                        .Last();
                var bodyObject = new {
                    issue = new {
                        title = $"[CF {i}] " +
                            issueRoot
                            .QuerySelector(".project-issue-header h2")
                            .ChildNodes
                            .Single(n => n.NodeType == HtmlNodeType.Text)
                            .InnerText
                            .Replace("&#x27;", "'")
                            .Replace("&quot;", "\""),

                        body = $"{firstComment.body} | Imported from CurseForge issue <a href=\"https://wow.curseforge.com/projects/tellmewhen/issues/{i}\">#{i}</a> | <a href=\"https://github.com/tmw-issue-import/dump/blob/master/html/{i}.html\">Raw</a>",
                        closed = closed,
                        closed_at = closed 
                            ? GetDate(issueRoot
                                .QuerySelectorAll(".project-issue-event.status-change.closed")
                                .LastOrDefault(), updatedDate)
                            : null,
                        created_at = firstComment.date,
                        updated_at = updatedDate,
                        labels = issueRoot
                            .QuerySelectorAll(".project-issue-header .project-issue-tags .tag-tag")
                            .Select(n => {
                                switch (n.InnerText.Replace("&#x27;", "'")) {
                                    case "Enhancment": return "Enhancement";
                                    case "Feature Already Exists": return "Invalid";
                                    case "Implemented": return "Resolved";
                                    case "Fixed": return "Resolved";
                                    case "Verified": return "Resolved";
                                    case "Won't Fix": return "wontfix";
                                    case "Can't Fix": return "cantfix";
                                    case "Accepted": return null;
                                    case "New": return null;
                                    case "On Hold": return null;
                                    case "Other": return null;
                                    case "Patch": return null;
                                    case "Replied": return null;
                                    case "Started": return null;
                                    case "Task": return null;
                                    case "Waiting": return null;
                                    default: return n.InnerText;
                                }
                                /*
                                
resolved
declined
cantfix
defect
duplicate
enhancement
invalid
question
wontfix 
 */
                            })
                            .Select(s => s?.ToLower())
                            .Where(s => s != null)
                            .Distinct(),
                    },

                    comments = comments
                        .Skip(1)
                        .Select(c => new {
                            body = c.body,
                            created_at = c.date
                        })
                };

                // Console.WriteLine(issue);

                string json = JsonConvert.SerializeObject(
                    bodyObject, 
                    Formatting.None,
                    new JsonSerializerSettings{
                        NullValueHandling = NullValueHandling.Ignore
                    }
                );
                File.WriteAllText($"json/{i}.json", json);

                HttpRequestMessage MakeMessage() {
                    var m = new HttpRequestMessage();
                    m.Headers.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("token", GHToken);
                    m.Headers.Accept.Add(
                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.golden-comet-preview+json"));
                    m.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("TMWIssueImporter", "0.1.0"));
                    return m;
                }

                var message = MakeMessage();
                message.Method = HttpMethod.Post;
                message.RequestUri = new Uri($"https://api.github.com/repos/{Repo}/import/issues");
                message.Content = new StringContent(json);

                var res = client.SendAsync(message).Result;

                var resBody = JsonConvert.DeserializeAnonymousType(
                    res.Content.ReadAsStringAsync().Result, new {
                    id = 0,
                    status = "",
                    url = "",
                });

                Console.WriteLine(i.ToString() + ": " + resBody.ToString());

                while (true) {
                    Thread.Sleep(1000);
                    message = MakeMessage();
                    message.Method = HttpMethod.Get;
                    message.RequestUri = new Uri(resBody.url);
                    var statusRes = client.SendAsync(message).Result;
                    var statusBody = JsonConvert.DeserializeAnonymousType(
                        statusRes.Content.ReadAsStringAsync().Result, new {
                        id = 0,
                        status = "",
                    });
                    Console.WriteLine(i.ToString() + ": " + statusBody.ToString());
                    if (statusBody.status == "imported") {
                        break;
                    } else if (statusBody.status == "failed"){
                        Thread.Sleep(100000);
                    }
                }
                
            //});
            }
        }
    }
}
