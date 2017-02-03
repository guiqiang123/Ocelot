﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CacheManager.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using Ocelot.ManualTest;
using Ocelot.Middleware;
using Shouldly;
using ConfigurationBuilder = Microsoft.Extensions.Configuration.ConfigurationBuilder;

namespace Ocelot.AcceptanceTests
{
    public class Steps : IDisposable
    {
        private TestServer _ocelotServer;
        private HttpClient _ocelotClient;
        private HttpResponseMessage _response;
        private HttpContent _postContent;
        private BearerToken _token;
        public HttpClient OcelotClient => _ocelotClient;
        public string RequestIdKey = "OcRequestId";

        public void GivenThereIsAConfiguration(FileConfiguration fileConfiguration)
        {
            var configurationPath = TestConfiguration.ConfigurationPath;

            var jsonConfiguration = JsonConvert.SerializeObject(fileConfiguration);

            if (File.Exists(configurationPath))
            {
                File.Delete(configurationPath);
            }

            File.WriteAllText(configurationPath, jsonConfiguration);
        }

        public void GivenThereIsAConfiguration(FileConfiguration fileConfiguration, string configurationPath)
        {
            var jsonConfiguration = JsonConvert.SerializeObject(fileConfiguration);

            if (File.Exists(configurationPath))
            {
                File.Delete(configurationPath);
            }

            File.WriteAllText(configurationPath, jsonConfiguration);
        }

        /// <summary>
        /// This is annoying cos it should be in the constructor but we need to set up the file before calling startup so its a step.
        /// </summary>
        public void GivenOcelotIsRunning()
        {
            _ocelotServer = new TestServer(new WebHostBuilder()
                .UseStartup<Startup>());

            _ocelotClient = _ocelotServer.CreateClient();
        }

        /// <summary>
        /// This is annoying cos it should be in the constructor but we need to set up the file before calling startup so its a step.
        /// </summary>
        public void GivenOcelotIsRunning(OcelotMiddlewareConfiguration ocelotMiddlewareConfig)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("configuration.json")
                .AddEnvironmentVariables();

            var configuration = builder.Build();

            _ocelotServer = new TestServer(new WebHostBuilder()
                .UseConfiguration(configuration)
                .ConfigureServices(s =>
                {
                    Action<ConfigurationBuilderCachePart> settings = (x) =>
                    {
                        x.WithMicrosoftLogging(log =>
                        {
                            log.AddConsole(LogLevel.Debug);
                        })
                        .WithDictionaryHandle();
                    };

                    s.AddOcelotOutputCaching(settings);
                    s.AddOcelotFileConfiguration(configuration);
                    s.AddOcelot();
                })
                .ConfigureLogging(l =>
                {
                    l.AddConsole(configuration.GetSection("Logging"));
                    l.AddDebug();
                })
                .Configure(a =>
                {
                    a.UseOcelot(ocelotMiddlewareConfig);
                }));

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenIHaveAddedATokenToMyRequest()
        {
            _ocelotClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token.AccessToken);
        }

        public void GivenIHaveAToken(string url)
        {
            var tokenUrl = $"{url}/connect/token";
            var formData = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("client_id", "client"),
                new KeyValuePair<string, string>("client_secret", "secret"),
                new KeyValuePair<string, string>("scope", "api"),
                new KeyValuePair<string, string>("username", "test"),
                new KeyValuePair<string, string>("password", "test"),
                new KeyValuePair<string, string>("grant_type", "password")
            };
            var content = new FormUrlEncodedContent(formData);

            using (var httpClient = new HttpClient())
            {
                var response = httpClient.PostAsync(tokenUrl, content).Result;
                var responseContent = response.Content.ReadAsStringAsync().Result;
                response.EnsureSuccessStatusCode();
                _token = JsonConvert.DeserializeObject<BearerToken>(responseContent);
            }
        }

        public void VerifyIdentiryServerStarted(string url)
        {
            using (var httpClient = new HttpClient())
            {
                var response = httpClient.GetAsync($"{url}/.well-known/openid-configuration").Result;
                response.EnsureSuccessStatusCode();
            }
        }


        public void WhenIGetUrlOnTheApiGateway(string url)
        {
            _response = _ocelotClient.GetAsync(url).Result;
        }

        public void WhenIGetUrlOnTheApiGatewayMultipleTimes(string url, int times)
        {
            var tasks = new Task[times];
            
            for (int i = 0; i < times; i++)
            {
                tasks[i] = _ocelotClient.GetAsync(url);
            }

            Task.WaitAll(tasks);
        }

        public void WhenIGetUrlOnTheApiGateway(string url, string requestId)
        {
            _ocelotClient.DefaultRequestHeaders.TryAddWithoutValidation(RequestIdKey, requestId);

            _response = _ocelotClient.GetAsync(url).Result;
        }

        public void WhenIPostUrlOnTheApiGateway(string url)
        {
            _response = _ocelotClient.PostAsync(url, _postContent).Result;
        }

        public void GivenThePostHasContent(string postcontent)
        {
            _postContent = new StringContent(postcontent);
        }

        public void ThenTheResponseBodyShouldBe(string expectedBody)
        {
            _response.Content.ReadAsStringAsync().Result.ShouldBe(expectedBody);
        }

        public void ThenTheStatusCodeShouldBe(HttpStatusCode expectedHttpStatusCode)
        {
            _response.StatusCode.ShouldBe(expectedHttpStatusCode);
        }

        public void Dispose()
        {
            _ocelotClient?.Dispose();
            _ocelotServer?.Dispose();
        }

        public void ThenTheRequestIdIsReturned()
        {
            _response.Headers.GetValues(RequestIdKey).First().ShouldNotBeNullOrEmpty();
        }

        public void ThenTheRequestIdIsReturned(string expected)
        {
            _response.Headers.GetValues(RequestIdKey).First().ShouldBe(expected);
        }
    }
}
