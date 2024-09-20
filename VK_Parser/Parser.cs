using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;
using System.Data.SqlTypes;
using System.Windows.Shapes;
using System.Windows;
using System.Threading;



namespace VK_Parser
{
    class Parser : INotifyPropertyChanged
    {
        private string? _url = "https://vk.com/club222678142";
        private bool _downloadFromComments = true;
        private bool _onlyAudio;
        private string? _storagePath = "D:\\Архив\\Хорошие песни";
        private string? _maxFileSize = "500";

        private bool isFirstRequest = true;
        private bool isNoLinks = true;

        Object locker = new();

        delegate string SetArguments(string link);

        enum LogVariant
        {
            Success,
            TooBigFile,
            AlreadyDownloaded,
            UnknownError
        }

        #region properties
        public string? url
        {
            get { return _url; }
            set
            {
                _url = value;
                NotifyPropertyChanged("url");
            }
        }
        public bool downloadFromComments
        {
            get { return _downloadFromComments; }
            set { _downloadFromComments = value; }
        }

        public bool onlyAudio
        {
            get { return _onlyAudio; }
            set { _onlyAudio = value;}
        }

        public string? storagePath
        {
            get { return _storagePath; }
            set
            {
                _storagePath = value;
                NotifyPropertyChanged("storagePath");
            }
        }

        public string? maxFileSize
        {
            get { return _maxFileSize; }
            set { _maxFileSize = value; }
        }

        #endregion

        #region INotifyPropertyChanged Members
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }
        #endregion

        public bool CheckValues()
        {
            return (url != "") && (storagePath != "") && (maxFileSize != "");
        }

        public async void Parse()
        {
            do
            {
                string? html = await GetHtml(url);
                if (html == null) return;

                List<Post> posts = new List<Post>();
                GetLinks(posts, html);

                DownloadContent(posts);
            } while (!isNoLinks);
        }

        private async Task<string?> GetHtml(string url)
        {
            // первые посты берем одним запросом, а последующие другим из-за специфики работы с api vk
            if (isFirstRequest) 
                return await GetFirstPosts(url);
            else                
                return await GetMorePosts(url);
        }

        private async Task<string?> GetFirstPosts(string url)
        {
            HttpClient httpClient = new HttpClient();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

            // добавляем заголовки
            httpClient.DefaultRequestHeaders.Add("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 YaBrowser/24.7.0.0 Safari/537.36");

            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(request); // выполняем запрос
                var byteArray = await response.Content.ReadAsByteArrayAsync();

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);      // настраиваем кодировку
                Encoding encoding = Encoding.GetEncoding("windows-1251");

                string result = encoding.GetString(byteArray, 0, byteArray.Length); // получаем html документ

                IHtmlDocument angle = new HtmlParser().ParseDocument(result);       // выбераем из всего hrml документа список постов
                return angle.GetElementById("page_wall_posts").InnerHtml;
            }
            catch(Exception ex)
            {
                Trace.WriteLine(ex);
                return null;
            }
        }

        private async Task<string> GetMorePosts(string url)
        {
            return "result";
        }

        private void GetLinks(List<Post> posts, string html)
        {
            string className = SetMode();                                       // выбираем парсить только посты или посты и комментарии

            IHtmlDocument angle = new HtmlParser().ParseDocument(html);
            foreach (IElement block in angle.GetElementsByClassName(className)) // рассматриваем каждую запись (и возможно комментарий)
            {
                Post newPost = new Post(block.Id);                              // обозначаем новый пост
                FilterLinks(block, newPost);                                    // оставляем только ссылки на музыку, записываем их в newPost
                if(newPost.links.Count > 0)
                    posts.Add(newPost);                                             
            }
        }

        private string SetMode()
        {
            if (downloadFromComments)
                return "post"; // комментарии + записи 
            else
                return "wall_text";  // только записи
        }

        private void FilterLinks(IElement post, Post newPost)
        {
            Regex youtubeLink = new Regex(@"((.*)youtube\.com(.*))|((.*)youtu\.be(.*))");   
            Regex vkLink = new Regex(@"video-(\w*)");   
            foreach (IElement link in post.QuerySelectorAll("a"))
            {
                try
                {
                    string? href = link.GetAttribute("href");
                    if (( youtubeLink.Match(href).Value != "" ) && LinkIsUnique(href, newPost.links))
                        newPost.links.Add(link.TextContent);

                    if ((vkLink.Match(href).Value != "") && LinkIsUnique("https://vk.com" + href, newPost.links))
                        newPost.links.Add( "https://vk.com" + href );
                }
                catch
                {
                    continue;
                }
            }
        }

        private bool LinkIsUnique(string newLink, List<string> links)
        {
            bool isUnique = true;
            foreach (string link in links)
            {
                if (link == newLink)
                    isUnique = false;
            }
            return isUnique;
        }

        private void DownloadContent(List<Post> posts)
        {
            foreach (Post post in posts)
            {
                foreach(string link in post.links)
                {
                    Thread myThread = new(DownloadByLink);
                    myThread.Name = $"Поток {link}";
                    myThread.Start(link);
                }
            }
        }

        private void DownloadByLink(object? obj)
        {
            if (obj is string link)
            {
                int exitCode = 0;
                string log = "";
                bool isProperSize = true;

                if (GetVideoSize(link) <= int.Parse(maxFileSize))
                    StartConsoleApp(link, SetDownloadArguments, out exitCode, out log);
                else
                    isProperSize = false;

                lock (locker)
                {
                    WriteLog(DetermineVariant(exitCode, log, isProperSize), GetVideoName(link));
                }
            }
        }

        private LogVariant DetermineVariant(int exitCode, string log, bool isProperSize)
        {
            if (log.Contains("has already been recorded in the archive"))
                return LogVariant.AlreadyDownloaded;
            else if (exitCode == 0)
                return LogVariant.Success;
            else if (!isProperSize)
                return LogVariant.TooBigFile;
            else
                return LogVariant.UnknownError;
        }

        private string GetVideoName(string link)
        {
            StartConsoleApp(link, SetVideoNameArguments, out int exitCode, out string output);
            if (exitCode == 0)
            {
                return output;
            }
            return "";
        }

        private int GetVideoSize(string link)
        {
            StartConsoleApp(link, SetFileSizeArguments, out int exitCode, out string output);
            if (exitCode == 0)
            {
                output = output.Remove(output.Length - 4);
                if (output == "NA") 
                    return 0;
                else
                    return int.Parse(output);
            }
            return -1;
        }

        private void StartConsoleApp(string link, Func<string, string> SetArguments, out int exitCode, out string log)
        {
            StringBuilder res = new StringBuilder();

            using Process process = new Process();
            {
                process.StartInfo.FileName = @"ffmpeg\yt-dlp.exe";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.Arguments = SetArguments(link);

                using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                            outputWaitHandle.Set();
                        else
                            res.AppendLine(e.Data);
                    };


                    process.Start();

                    process.BeginOutputReadLine();

                    process.WaitForExit();
                    outputWaitHandle.WaitOne();
                }
            };

            exitCode = process.ExitCode;
            log = res.ToString();
        }

        private string SetDownloadArguments(string link)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("-P " + "\"" + storagePath + "\"" + " ");      // куда сохранять
            stringBuilder.Append("--max-filesize " + maxFileSize + "M" + " ");  // максимальный допустимый размер для скачивания (в МБ)
            stringBuilder.Append("--force-write-archive" + " ");                // 
            stringBuilder.Append("--download-archive " + "\"" + storagePath + "\\archive.txt\"" + " "); // 
            if(onlyAudio) stringBuilder.Append("--extract-audio" + " ");        // возможность скачать только аудио

            stringBuilder.Append(CutYouTubeLink(link) + " ");
            return stringBuilder.ToString();
        }

        private string SetFileSizeArguments(string link)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(link + " ");
            stringBuilder.Append("-O \"%(requested_formats.0.filesize.:-6+requested_formats.1.filesize.:-6)dMB\"");

            return stringBuilder.ToString();
        }

        private string SetVideoNameArguments(string link)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("--print \"%(title)s\" ");
            stringBuilder.Append(link);

            return stringBuilder.ToString();
        }

        private void WriteLog(LogVariant logVariant , string videoName)
        {
            string message = "";
            switch (logVariant)
            {
                case LogVariant.Success:
                    message = "Загружен файл " + videoName + "\n";
                    break;

                case LogVariant.TooBigFile:
                    message = "Файл не загружен - превышен допустимый размер " + videoName + "\n";
                    break;

                case LogVariant.AlreadyDownloaded:
                    message = "Загрузка отменена - файл уже загружен " + videoName + "\n";
                    break;

                case LogVariant.UnknownError:
                    message = "Не удалось загрузить файл " + videoName + "\n";
                    break;
            }

            MainWindow.main.Log = message;
        }

        private string CutYouTubeLink(string link)
        {
            int index = link.IndexOf("&");
            if(index != -1)
                return link.Substring(0, index);
            else 
                return link;
        }
    }

    class Post
    {
        public string id;
        public List<string> links = new();

        public Post(string id)
        {
            this.id = id;
        }
    }
}