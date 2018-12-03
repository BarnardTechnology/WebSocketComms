using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MimeTypes;

namespace BarnardTech.WebSocketComms
{
    internal class WebContentGenerator
    {
        Dictionary<string, string> _pages;
        Dictionary<string, byte[]> _files;
        string _rootPath;

        public WebContentGenerator(Type ContentRoot)
        {
            _rootPath = ContentRoot.FullName.Substring(0, ContentRoot.FullName.LastIndexOf("."));

            _pages = new Dictionary<string, string>();
            _files = new Dictionary<string, byte[]>();

            //Assembly a = Assembly.GetExecutingAssembly();
            Assembly a = Assembly.GetAssembly(ContentRoot);
            foreach (string name in a.GetManifestResourceNames())
            {
                if (MimeTypeMap.GetMimeType(Path.GetExtension(name)).Contains("text"))
                {
                    using (Stream stream = a.GetManifestResourceStream(name))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string pageContent = reader.ReadToEnd();
                        _pages.Add(name, pageContent);
                    }
                }
                else
                {
                    using (Stream stream = a.GetManifestResourceStream(name))
                    {
                        byte[] streamBytes = new byte[stream.Length];
                        stream.Read(streamBytes, 0, streamBytes.Length);
                        _files.Add(name, streamBytes);
                    }
                }
            }
        }

        public string GetMimeType(string path)
        {
            path = checkPath(path);
            return MimeTypeMap.GetMimeType(Path.GetExtension(path));
        }

        /// <summary>
        /// Check incoming path to make sure it's going to match up correctly with a file.
        /// Currently, we're using this to make sure a default document is loaded when the
        /// path is '/', but it'll probably be useful for other reasons later on.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        string checkPath(string path)
        {
            if (path == "/")
            {
                path = path + "index.html";
            }
            return path;
        }

        public bool ContentExists(string path)
        {
            path = checkPath(path);
            path = _rootPath + path.Replace("/", ".");
            if (_files.ContainsKey(path))
            {
                return true;
            }
            else if (_pages.ContainsKey(path))
            {
                return true;
            }
            return false;
        }

        public byte[] GetContent(string path)
        {
            path = checkPath(path);
            path = _rootPath + path.Replace("/", ".");
            if (_files.ContainsKey(path))
            {
                return _files[path];
            }
            else
            {
                string content = GetPage(path);
                if (content != "")
                {
                    return System.Text.Encoding.UTF8.GetBytes(content);
                }
                else
                {
                    return new byte[0];
                }
            }
        }

        public string GetPage(string path)
        {
            path = checkPath(path);
            Assembly a = Assembly.GetExecutingAssembly();

            if (_pages.ContainsKey(path))
            {
                string outContent = _pages[path];
                Regex r = new Regex(@"\[#.*\]");
                MatchCollection matches = r.Matches(outContent);

                if (matches.Count > 0)
                {
                    foreach (Match m in matches)
                    {
                        if (m.Value.StartsWith("[#INCLUDE(\""))
                        {
                            string includeFile = m.Value.Replace("[#INCLUDE(\"", "").Replace("\")]", "");
                            outContent = outContent.Replace(m.Value, GetPage("HectorWeb" + includeFile.Replace("/", ".")));
                        }
                    }
                }

                return outContent;
            }
            else
            {
                return "";
            }
        }

        public void SetPage(string path, string pageContent)
        {
            path = _rootPath + path.Replace("/", ".");
            if (_pages.ContainsKey(path))
            {
                _pages[path] = pageContent;
            }
            else
            {
                _pages.Add(path, pageContent);
            }
        }

        public void AppendPage(string path, string pageContent)
        {
            SetPage(path, GetPage(path) + pageContent);
        }
    }
}