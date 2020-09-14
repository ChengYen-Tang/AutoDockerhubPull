using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AutoDockerhubPull
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string username = args[0];
            string password = args[1];
            string organization = args[2];

            string token = await GetToken(username, password);
            IReadOnlyList<string> repositories = await GetRepositories(organization, token);
            IEnumerable<Task<IEnumerable<string>>> getImagesTasks = repositories.Select(repository => GetImages(organization, repository, token));
            IEnumerable<string> images = (await Task.WhenAll(getImagesTasks)).SelectMany(image => image);

            Console.WriteLine(string.Join('\n', images));

            GeneratePipelineYML(images);
        }

        /// <summary>
        /// 取得用於存取 dockerhub api 的 token
        /// </summary>
        /// <param name="username">dockerhub 帳號</param>
        /// <param name="password">dockerhub 密碼</param>
        /// <returns></returns>
        private static async Task<string> GetToken(string username, string password)
        {
            Dictionary<string, string> body = new Dictionary<string, string>()
            {
                { "username", username },
                { "password", password }
            };
            Uri tokenUrl = new Uri($"https://hub.docker.com/v2/users/login/");

            var formData = new FormUrlEncodedContent(body);
            HttpClient tokenClient = HttpClientFactory.Create();
            var tokenBuffer = await tokenClient.PostAsync(tokenUrl, formData);
            string tokenJson = await tokenBuffer.Content.ReadAsStringAsync();
            JObject jObject = JObject.Parse(tokenJson);
            if (jObject.Property("token") is null)
                throw new Exception("get token failed.");

            return jObject["token"].ToString();
        }

        /// <summary>
        /// 取得組織內的所有存放庫
        /// </summary>
        /// <param name="organization"> 組織名稱 </param>
        /// <param name="token"> 用於存取組織的 token </param>
        /// <returns></returns>
        private static async Task<IReadOnlyList<string>> GetRepositories(string organization, string token)
        {
            string nextUri = $"https://hub.docker.com/v2/repositories/{organization}/";
            List<string> repositories = new List<string>();

            while (!string.IsNullOrEmpty(nextUri))
            {
                string jsonString = await CallApi(nextUri, token);
                JObject jObject = JObject.Parse(jsonString);
                if (jObject.Property("detail") != null)
                    throw new Exception("origin not found.");

                nextUri = jObject.Property("next") is null ? string.Empty : jObject["next"].ToString();
                repositories.AddRange(jObject["results"].Select(item => item.Value<string>("name")));
            }

            return repositories;
        }

        /// <summary>
        /// 取得組織內的 image
        /// </summary>
        /// <param name="organization"> 組織名稱 </param>
        /// <param name="repository"> 存放庫名稱 </param>
        /// <param name="token"> 用於存取組織的 token </param>
        /// <returns></returns>
        private static async Task<IEnumerable<string>> GetImages(string organization, string repository, string token)
        {
            string nextUri = $"https://hub.docker.com/v2/repositories/{organization}/{repository}/tags/";
            List<string> tags = new List<string>();

            while (!string.IsNullOrEmpty(nextUri))
            {
                string jsonString = await CallApi(nextUri, token);
                JObject jObject = JObject.Parse(jsonString);
                if (jObject["count"].ToString() == "0")
                    throw new Exception("repository not found.");

                nextUri = jObject.Property("next") is null ? string.Empty : jObject["next"].ToString();
                tags.AddRange(jObject["results"].Select(item => item.Value<string>("name")));
            }

            return tags.Select(tag => $@"""{organization}/{repository}:{tag}""");
        }

        /// <summary>
        /// 將 images 寫入 run-docker.yml
        /// </summary>
        /// <param name="images"> 映像檔 </param>
        /// <returns></returns>
        private static void GeneratePipelineYML(IEnumerable<string> images)
        {
            string content = File.ReadAllText("./azure-pipelines/run-docker-template.yml");
            content = content.Replace("{0}", DateTime.UtcNow.ToString());
            content = content.Replace("{1}", string.Join(", ", images));
            File.WriteAllText("./azure-pipelines/run-docker.yml", content);
        }

        /// <summary>
        /// 呼叫 dockerhub api 取得 image or image tag
        /// </summary>
        /// <param name="uri"> api url </param>
        /// <param name="token"> 用於存取組織的 token </param>
        /// <returns></returns>
        private static async Task<string> CallApi(string uri, string token)
        {
            Uri repositoryUrl = new Uri(uri);
            HttpClient repositoryClient = HttpClientFactory.Create();
            repositoryClient.DefaultRequestHeaders.Add("Authorization", $"JWT {token}");
            var repositoryBuffer = await repositoryClient.GetAsync(repositoryUrl);
            return await repositoryBuffer.Content.ReadAsStringAsync();
        }
    }
}
