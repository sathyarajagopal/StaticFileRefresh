using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Xml.Linq;

namespace StaticFileRefresh.Helpers
{
    public static class JavascriptExtension
    {
        public static MvcHtmlString IncludeVersionedJs(this HtmlHelper helper, string relativeFilePathQuery)
        {
            string version = GetVersion(helper, relativeFilePathQuery);
            return MvcHtmlString.Create("<script type='text/javascript' src='" + relativeFilePathQuery + version + "'></script>");
        }

        private static string GetVersion(this HtmlHelper helper, string relativeFilePathQuery)
        {
            var context = helper.ViewContext.RequestContext.HttpContext;
            string relativeFilePath = relativeFilePathQuery.Split('?')[0];

            if (context.Cache[relativeFilePath] == null)
            {
                var physicalPath = context.Server.MapPath(relativeFilePath);
                var version = $"&v={new System.IO.FileInfo(physicalPath).LastWriteTime.ToString("yyyyMMddHHmmss")}";
                string dependencyFile1 = physicalPath;
                string dependencyFile2 = @"C:\inetpub\wwwroot\Publish\StaticFileRefresh\FileReferenceMapper.config";
                string[] dependencies = new string[] { dependencyFile1, dependencyFile2 };
                context.Cache.Insert(relativeFilePath, version, new CacheDependency(dependencies));
                return version;
            }
            else
            {
                return context.Cache[relativeFilePath] as string;
            }
        }
    }

    public class FileBundleHandler: IHttpHandler
    {
        private static readonly StringDictionary allowedFileTypes = new StringDictionary() { { "js", "application/javascript" } };
        private List<FileReferenceEntity> _staticFiles;
        public List<FileReferenceEntity> StaticFiles
        {
            get
            {
                _staticFiles = FileReferenceEntity.GetFileReferences();
                return _staticFiles;
            }
        }

        public bool IsReusable { get { return true; } }

        public void ProcessRequest(HttpContext context)
        {
            Action<string, string, string> bustCacheAndRedirect = (srcApp, jsPathValueinFileReferenceMapper, extension) =>
               {
                   string cacheKey = srcApp + "_FileContent";
                   string absolutePath = HostingEnvironment.MapPath(jsPathValueinFileReferenceMapper);
                   string cfgFRMabsPath = @"C:\inetpub\wwwroot\Publish\StaticFileRefresh\FileReferenceMapper.config";
                   FileInfo[] files = BundleHelper.FindFiles(absolutePath + "," + cfgFRMabsPath);
                   DateTime lastModified = files.Max(f => f.LastWriteTime);
                   string dependencyFile1 = absolutePath;
                   string dependencyFile2 = cfgFRMabsPath;
                   string[] dependencies = new string[] { dependencyFile1, dependencyFile2 };
                   if (CheckStatus304(lastModified))
                   {
                       context.Response.Write(string.Empty);
                   }
                   else
                   {
                       string cachedContent = (string)HttpContext.Current.Cache[cacheKey];
                       if (string.IsNullOrWhiteSpace(cachedContent))
                       {
                           cachedContent = ReadContent(files);
                           HttpContext.Current.Cache.Insert(cacheKey, cachedContent, new CacheDependency(dependencies));
                       }
                       context.Response.Write(cachedContent);
                   }
                   int index = jsPathValueinFileReferenceMapper.LastIndexOf('/') + 1;
                   string newPath = jsPathValueinFileReferenceMapper.Insert(index, "v_" + lastModified.ToString("yyyyMMddHHmmss") + "/");
                   SetHeaders(files, allowedFileTypes[extension], newPath);
               };

            string referringSourceApp = context.Request.Params != null && !string.IsNullOrWhiteSpace(context.Request.Params["s"]) ? context.Request.Params["s"] : "en";
            string fileExtension = Path.GetExtension(context.Request.PhysicalPath).ToLowerInvariant();

            foreach (FileReferenceEntity file in StaticFiles)
            {
                if (string.Equals(file.Key, referringSourceApp, StringComparison.OrdinalIgnoreCase))
                {
                    bustCacheAndRedirect(referringSourceApp, file.Value, fileExtension);
                    break;
                }
            }
        }

        private string ReadContent(FileInfo[] files)
        {
            StringBuilder sb = new StringBuilder(1024);
            Array.ForEach(files, f =>
             {
                 string fileExtension = Path.GetExtension(f.Name).ToLowerInvariant();

                 if (fileExtension.Equals(".js"))
                 {
                     sb.AppendLine(File.ReadAllText(f.FullName));
                 }
             });
            return sb.ToString();
        }

        private void SetHeaders(FileInfo[] files, string mimeType, string newPath)
        {
            DateTime lastModified = files.Max(f => f.LastWriteTime);
            HttpContext context = HttpContext.Current;
            context.Response.AddFileDependencies(files.Select(f => f.FullName).ToArray());
            context.Response.ContentType = mimeType;
            context.Response.AppendHeader("FileVersion", newPath);
            context.Response.Cache.SetLastModified(lastModified);
            context.Response.Cache.SetCacheability(HttpCacheability.ServerAndPrivate);
            context.Response.Cache.SetExpires(DateTime.Now.AddYears(1));
            context.Response.Cache.SetOmitVaryStar(true);
            context.Response.Cache.SetValidUntilExpires(true);
        }

        private bool CheckStatus304(DateTime lastModified)
        {
            bool flag = false;
            HttpContext context = HttpContext.Current;
            if (!string.IsNullOrWhiteSpace(context.Request.Headers["If-Modified-Since"]))
            {
                var modifiedSince = DateTime.ParseExact(context.Request.Headers["If-Modified-Since"], "r", CultureInfo.InvariantCulture).ToLocalTime();
                modifiedSince = new DateTime(modifiedSince.Year, modifiedSince.Month, modifiedSince.Day, modifiedSince.Hour, modifiedSince.Minute, modifiedSince.Second);
                lastModified = new DateTime(lastModified.Year, lastModified.Month, lastModified.Day, lastModified.Hour, lastModified.Minute, lastModified.Second);
                if (modifiedSince.CompareTo(lastModified) == 0)
                {
                    context.Response.StatusCode = 304;
                    context.Response.StatusDescription = "Not modified";
                    flag = true;
                }
            }
            return flag;
        }
    }

    public class FileReferenceEntity
    {
        public string Key { get; set; }
        public string Value { get; set; }

        public static List<FileReferenceEntity> GetFileReferences()
        {
            XDocument xDoc = XDocument.Load(@"C:\inetpub\wwwroot\Publish\StaticFileRefresh\FileReferenceMapper.config");
            List<FileReferenceEntity> items = (from element in xDoc.Descendants("configuration").Descendants("appSettings").Elements("add")
                                               select new FileReferenceEntity
                                               {
                                                   Key = element.Attribute("key").Value,
                                                   Value = element.Attribute("value").Value
                                               }).ToList();
            return items;
        }
    }

    public static class BundleHelper
    {
        internal static FileInfo[] FindFiles(string absoluteFileName)
        {
            List<FileInfo> fileInfoList = new List<FileInfo>();
            FileInfo[] fileInfoArr = null;
            string[] arrAbsFileNames = string.IsNullOrWhiteSpace(absoluteFileName) ? null : absoluteFileName.Split(',');
            if (arrAbsFileNames != null)
            {
                for (int i = 0; i < arrAbsFileNames.Length; i++)
                {
                    if (arrAbsFileNames[i] != null)
                    {
                        string absFileName = arrAbsFileNames[i];
                        if (!string.IsNullOrWhiteSpace(absFileName) && File.Exists(absFileName))
                        {
                            fileInfoList.Add(new FileInfo(absFileName));

                        }
                    }
                }
            }
            if (fileInfoList.Any())
            {
                return fileInfoList.ToArray();
            }
            string dir = absoluteFileName.Replace(Path.GetExtension(absoluteFileName), string.Empty);
            fileInfoArr = new DirectoryInfo(dir).GetFiles("*" + Path.GetExtension(absoluteFileName));
            return fileInfoArr;
        }
    }
}