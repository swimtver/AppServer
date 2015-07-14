﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;
using Castle.Core.Logging;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Inceptum.AppServer.Configuration;
using Inceptum.AppServer.Model;

namespace Inceptum.AppServer.WebApi.Controllers
{
    /// <summary>
    /// Configurations
    /// </summary>
    public class ConfigurationsController : ApiController
    {
        private readonly IManageableConfigurationProvider m_Provider;
        private readonly ILogger m_Logger;

        public ConfigurationsController(IManageableConfigurationProvider provider, ILogger logger)
        {
            m_Logger = logger;
            m_Provider = provider;
        }



        /// <summary>
        /// List all configurations.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ResponseType(typeof(ConfigurationInfo[]))]
        public IHttpActionResult Index()
        {
            return Ok(m_Provider.GetConfigurations());
        }

        /// <summary>
        /// Retrieve configuration.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns></returns>
        [HttpGet]
        [ResponseType(typeof(ConfigurationInfo))]
        public IHttpActionResult Get(string id)
        {
            return Ok(m_Provider.GetConfiguration(id));
        }

        /// <summary>
        /// Delete configuration.
        /// </summary>
        /// <param name="id">The configuration.</param>
        /// <returns></returns>
        [HttpDelete]
        public IHttpActionResult Delete(string id)
        {
            m_Provider.DeleteConfiguration(id);
            return Ok();
        }

        /// <summary>
        /// Create configuration.
        /// </summary>
        /// <param name="info">The configuration.</param>
        /// <returns></returns>
        [HttpPost]
        [ResponseType(typeof(ConfigurationInfo))]
        public IHttpActionResult Create(ConfigurationInfo info)
        {
            m_Provider.CreateConfiguration(info.Name);
            return Ok(m_Provider.GetConfiguration(info.Name));
        }

        /// <summary>
        /// Update configuration bundle.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="id">The bundle.</param>
        /// <param name="info">The bundle information.</param>
        /// <returns></returns>
        [HttpPut]
        [ResponseType(typeof(BundleInfo))]
        public IHttpActionResult UpdateBundle(string configuration, string id, BundleInfo info)
        {
            return Ok(m_Provider.CreateOrUpdateBundle(configuration, info.id, info.PureContent));
        }
        //TODO[KN]: arrange this update/create methsods look strange

        /// <summary>
        /// Create configuration bundle.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="info">The bundle information.</param>
        /// <returns></returns>
        [HttpPost]
        [ResponseType(typeof(BundleInfo))]
        public IHttpActionResult CreateBundle(string configuration, BundleInfo info)
        {
            info.id = string.IsNullOrEmpty(info.Parent) ? info.Name : info.Parent + "." + info.Name;
            return Ok(m_Provider.CreateOrUpdateBundle(configuration, info.id, info.PureContent));
        }

        /// <summary>
        /// Delete configuration bundle.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="id">The bundle.</param>
        [HttpDelete]
        public IHttpActionResult DeleteBundle(string configuration, string id)
        {
            m_Provider.DeleteBundle(configuration, id);
           return Ok();
        }


        /// <summary>
        /// Retrieve configuration bundle content.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="id">The bundle.</param>
        /// <param name="overrides">The overrides.</param>
        /// <returns></returns>
        [HttpGet]
        [ResponseType(typeof (string))]
        public IHttpActionResult GetBundle(string configuration, string id)
        {
            return GetBundleWithOverrides(configuration, id);
        }


        /// <summary>
        /// Retrieve configuration bundle content.
        /// </summary>
        /// <remarks>
        /// If overrides are provided. Bundle is searched from longest path. E.g if bundle "some.bundle" configuration 'someConf' is requested
        /// by uri /api/configurations/someConf/some.bundle/A/B ,  bundles would be tried in following order:
        /// some.bundle.A.B,  some.bundle.A,  some.bundle  (the first found would be returned)
        /// </remarks>
        /// <param name="configuration">The configuration.</param>
        /// <param name="bundle">The bundle.</param>
        /// <param name="overrides">The overrides.</param>
        /// <returns></returns>
        [HttpGet]
        [ResponseType(typeof(string))]
        public IHttpActionResult GetBundleWithOverrides(string configuration, string bundle,string overrides=null)
        {
            try
            {
                var bundleContent = m_Provider.GetBundle(configuration, bundle, overrides == null ? new string[0] : overrides.Split(new[] { '/' }));
                return Ok (bundleContent);
            }
            catch (BundleNotFoundException e)
            {
                return NotFound();

                //return new OperationResult.NotFound { Description = e.Message, ResponseResource = e.Message, Title = "Error" };
            }
            catch (Exception e)
            {
                return InternalServerError(e);
                //return new OperationResult.InternalServerError { Description = e.Message, ResponseResource = e.Message, Title = "Error" };
            }
        }


        /// <summary>
        /// Import configuration.
        /// </summary>
        /// <param name="id">The configuration.</param>
        /// <param name="file">The file.</param>
        /// <returns></returns>
        [HttpPost]
        public IHttpActionResult Import(string id, HttpPostedFileBase file)
        {
            var memoryStream = new MemoryStream();
            file.InputStream.CopyTo(memoryStream);
            var zipFile = new ZipFile(memoryStream);

            m_Provider.DeleteConfiguration(id);
            m_Provider.CreateConfiguration(id);

            int i = 0;
            foreach (ZipEntry bundleFile in zipFile)
            {
                i++;
                m_Logger.InfoFormat(bundleFile.Name);
                m_Provider.CreateOrUpdateBundle(id, bundleFile.Name, new StreamReader(zipFile.GetInputStream(bundleFile)).ReadToEnd());
            }
            m_Logger.InfoFormat("{0}", i);
            return Ok();
        }

        /// <summary>
        /// Export configuration.
        /// </summary>
        /// <param name="id">The configuration.</param>
        /// <returns></returns>
        [HttpGet]
        public IHttpActionResult Export(string id)
        {
            try
            {
                var config = m_Provider.GetConfiguration(id);
                var outputStream = new MemoryStream();
                var zipStream = new ZipOutputStream(outputStream);

                zipStream.SetLevel(3); //0-9, 9 being the highest level of compression


                Func<BundleInfo, BundleInfo[]> select = null;
                select = bundle => new[] { bundle }.Concat(bundle.Bundles.SelectMany(b => select(b))).ToArray();
                IEnumerable<BundleInfo> bundles = config.Bundles.SelectMany(b => @select(b));

                foreach (var bundle in bundles)
                {
                    var newEntry = new ZipEntry(bundle.id);
                    newEntry.DateTime = DateTime.Now;

                    zipStream.PutNextEntry(newEntry);
                    var memStreamIn = new MemoryStream(Encoding.UTF8.GetBytes(bundle.PureContent));
                    StreamUtils.Copy(memStreamIn, zipStream, new byte[4096]);
                    zipStream.CloseEntry();
                }

                zipStream.IsStreamOwner = false;    // False stops the Close also Closing the underlying stream.
                zipStream.Close();          // Must finish the ZipOutputStream before using outputMemStream.
                outputStream.Seek(0,SeekOrigin.Begin);
                return new FileResult(outputStream, config.Name + ".zip");
            }
            catch (Exception e)
            {
                return InternalServerError(e);
            }
        }
    }

    class FileResult : IHttpActionResult
    {
        private readonly Stream m_Stream;
        private readonly string m_ContentType;
        private string m_FileName;

        public FileResult(Stream stream,string fileName, string contentType = null)
        {
            m_FileName = fileName;
            m_Stream = stream;
            if (stream == null) throw new ArgumentNullException("stream");

            m_ContentType = contentType;
        }

        public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(m_Stream)
                };

                var contentType = m_ContentType ?? MimeMapping.GetMimeMapping(Path.GetExtension(m_FileName));
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = m_FileName
                };
                return response;

            }, cancellationToken);
        }
    }
}