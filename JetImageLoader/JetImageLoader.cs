﻿
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using JetImageLoader.Cache;

namespace JetImageLoader
{
    public class JetImageLoader
    {
        /// <summary>
        /// Used for log output as first symbols
        /// </summary>
        private const string JetImageLoaderLogTag = "[JetImageLoader]";

        private static readonly object LockObject = new object();

        private static JetImageLoader _instance;

        /// <summary>
        /// Gets singleton JetImageLoader instance
        /// </summary>
        public static JetImageLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (LockObject)
                    {
                        if (_instance == null) _instance = new JetImageLoader();
                    }
                }

                return _instance;
            }
        }
        
        protected JetImageLoaderConfig Config;

        protected JetImageLoader()
        {
        }

        /// <summary>
        /// Initializing JetImageLoader with setted configuration
        /// Initializing will be done only once, so other invokes of this method will do nothing
        /// </summary>
        /// <param name="jetImageLoaderConfig"></param>
        public void Initialize(JetImageLoaderConfig jetImageLoaderConfig)
        {
            if (jetImageLoaderConfig == null) throw new ArgumentException("Can not initialize JetImageLoader with empty configuration");
            if (Config != null) return;

            Config = jetImageLoaderConfig;
        }

        private void CheckConfig()
        {
            if (Config == null) throw new InvalidOperationException("JetImageLoader configuration was not setted, please Initialize JetImageLoader instance with JetImageLoaderConfiguration");
        }

        /// <summary>
        /// Async loading image from cache or network
        /// </summary>
        /// <param name="imageUrl">Url of the image to load</param>
        /// <returns>BitmapImage if load was successfull or null otherwise</returns>
        public async Task<BitmapImage> LoadImage(string imageUrl)
        {
            return await LoadImage(new Uri(imageUrl));
        }

        /// <summary>
        /// Async loading image from cache or network
        /// </summary>
        /// <param name="imageUri">Uri of the image to load</param>
        /// <returns>BitmapImage if load was successfull or null otherwise</returns>
        public async Task<BitmapImage> LoadImage(Uri imageUri)
        {
            CheckConfig();
            var bitmapImage = new BitmapImage();
            bitmapImage.SetSource(await LoadImageStream(imageUri));
            return bitmapImage;
        }

        /// <summary>
        /// Async loading image stream from cache or network
        /// </summary>
        /// <param name="imageUri">Uri of the image to load</param>
        /// <returns>Stream of the image if load was successfull, null otherwise</returns>
        public async Task<Stream> LoadImageStream(Uri imageUri)
        {
            CheckConfig();

            var imageUrl = imageUri.ToString();

            if (Config.CacheMode != CacheMode.NoCache)
            {
                var resultFromCache = await LoadImageStreamFromCache(imageUrl);
                if (resultFromCache != null) return resultFromCache;
            }

            try
            {
                Log("[network] loading " + imageUrl);
                var downloadResult = await Config.DownloaderImpl.DownloadAsync(imageUri);

                if (downloadResult.Exception != null || downloadResult.ResultStream == null)
                {
                    Log("[error] failed to download: " + imageUrl);
                    return null;
                }

                Log("[network] loaded " + imageUrl);

                if (Config.CacheMode != CacheMode.NoCache)
                {
                    if (Config.CacheMode == CacheMode.MemoryAndStorageCache || Config.CacheMode == CacheMode.OnlyMemoryCache) Config.MemoryCacheImpl.Put(imageUrl, downloadResult.ResultStream);

                    if (Config.CacheMode == CacheMode.MemoryAndStorageCache || Config.CacheMode == CacheMode.OnlyStorageCache)
                    {
                        // Async saving to the storage cache
                        // ReSharper disable once CSharpWarnings::CS4014
                        Config.StorageCacheImpl.Save(imageUrl, downloadResult.ResultStream);
                    }
                }

                return downloadResult.ResultStream;
            }
            catch
            {
                Log("[error] failed to save loaded image: " + imageUrl);
            }

            // May be another thread has saved image to the cache
            // It is real working case
            if (Config.CacheMode != CacheMode.NoCache)
            {
                var resultFromCache = await LoadImageStreamFromCache(imageUrl);
                if (resultFromCache != null) return resultFromCache;
            }

            Log("[error] failed to load image stream from cache and network: " + imageUrl);

            return null;
        }

        /// <summary>
        /// Loads image stream from memory or storage cachecache
        /// </summary>
        /// <param name="imageUrl"></param>
        /// <returns>Steam of the image or null if it was not found in cache</returns>
        protected async Task<Stream> LoadImageStreamFromCache(string imageUrl)
        {
            if (Config.CacheMode == CacheMode.MemoryAndStorageCache || Config.CacheMode == CacheMode.OnlyMemoryCache)
            {
                Stream memoryStream;

                if (Config.MemoryCacheImpl.TryGetValue(imageUrl, out memoryStream))
                {
                    Log("[memory] " + imageUrl);
                    return memoryStream;
                }
            }

            if (Config.CacheMode == CacheMode.MemoryAndStorageCache ||
                Config.CacheMode == CacheMode.OnlyStorageCache)
            {
                if (Config.StorageCacheImpl.IsCacheExists(imageUrl))
                {
                    Log("[storage] " + imageUrl);
                    var storageStream = await Config.StorageCacheImpl.LoadCacheStream(imageUrl);

                    // Moving cache to the memory
                    if (Config.CacheMode == CacheMode.MemoryAndStorageCache)
                        Config.MemoryCacheImpl.Put(imageUrl, storageStream);

                    return storageStream;
                }
            }

            return null;
        }

        /// <summary>
        /// Outputs log messages if IsLogEnabled
        /// </summary>
        /// <param name="message">to output</param>
        internal static void Log(string message)
        {
            if (Instance != null && Instance.Config.IsLogEnabled) Debug.WriteLine("{0} {1}", JetImageLoaderLogTag, message);
        }
    }
}