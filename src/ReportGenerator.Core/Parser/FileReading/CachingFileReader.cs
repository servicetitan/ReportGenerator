﻿using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Palmmedia.ReportGenerator.Core.Common;
using Palmmedia.ReportGenerator.Core.Properties;
using System.Net.Http.Headers;

namespace Palmmedia.ReportGenerator.Core.Parser.FileReading
{
    /// <summary>
    /// File reader with caching.
    /// Local files are read from disk. Remote files get downloaded and cached for a given period of time.
    /// </summary>
    internal class CachingFileReader : IFileReader
    {
        /// <summary>
        /// The HttpClient to retrieve remote files.
        /// </summary>
        private readonly HttpClient HttpClient;

        /// <summary>
        /// <see cref="IFileReader"/> for loading files from local disk.
        /// </summary>
        private readonly IFileReader localFileReader;

        /// <summary>
        /// The caching duration of code files that are downloaded from remote servers in minutes.
        /// </summary>
        private readonly int cachingDurationOfRemoteFilesInMinutes;

        /// <summary>
        /// Initializes a new instance of the <see cref="CachingFileReader" /> class.
        /// </summary>
        /// <param name="localFileReader"><see cref="IFileReader"/> for loading files from local disk.</param>
        /// <param name="cachingDurationOfRemoteFilesInMinutes">The caching duration of code files that are downloaded from remote servers in minutes.</param>
        /// <param name="authToken">Authorization token (will be used with private repos)</param>
        public CachingFileReader(IFileReader localFileReader, int cachingDurationOfRemoteFilesInMinutes, string authToken)
        {
            this.localFileReader = localFileReader;
            this.cachingDuringOfRemoteFilesInMinutes = cachingDurationOfRemoteFilesInMinutes;
            HttpClient = new HttpClient();
            if (authToken != null)
            {
                HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", authToken);
            }
        }

        /// <summary>
        /// Loads the file with the given path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="error">Error message if file reading failed, otherwise <code>null</code>.</param>
        /// <returns><code>null</code> if an error occurs, otherwise the lines of the file.</returns>
        public string[] LoadFile(string path, out string error)
        {
            try
            {
                if (path.StartsWith("http://") || path.StartsWith("https://"))
                {
                    string cacheDirectory = Path.Combine(Path.GetTempPath(), "ReportGenerator_Cache");

                    // GetHashCode does not produce same hash on every run (See: https://andrewlock.net/why-is-string-gethashcode-different-each-time-i-run-my-program-in-net-core/)
                    string cachedFile = Path.Combine(cacheDirectory, GetSha1Hash(path) + ".txt");

                    try
                    {
                        if (!Directory.Exists(cacheDirectory))
                        {
                            Directory.CreateDirectory(cacheDirectory);
                        }

                        if (File.Exists(cachedFile)
                            && File.GetLastWriteTime(cachedFile).AddMinutes(this.cachingDurationOfRemoteFilesInMinutes) > DateTime.Now)
                        {
                            error = null;
                            string[] cachedLines = File.ReadAllLines(cachedFile);
                            return cachedLines;
                        }
                    }
                    catch
                    {
                        // Ignore error, file gets downloaded in next step
                    }

                    string content = HttpClient.GetStringAsync(path).ConfigureAwait(false).GetAwaiter().GetResult();
                    string[] lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

                    try
                    {
                        if (this.cachingDurationOfRemoteFilesInMinutes > 0)
                        {
                            File.WriteAllLines(cachedFile, lines);
                        }
                    }
                    catch
                    {
                        // Ignore error, caching is not important
                    }

                    error = null;
                    return lines;
                }
                else
                {
                    return this.localFileReader.LoadFile(path, out error);
                }
            }
            catch (Exception ex)
            {
                error = string.Format(CultureInfo.InvariantCulture, " " + Resources.ErrorDuringReadingFile, path, ex.GetExceptionMessageForDisplay());
                return null;
            }
        }

        private static string GetSha1Hash(string input)
        {
            return string.Concat(new SHA1Managed().ComputeHash(Encoding.UTF8.GetBytes(input)).Select(x => x.ToString("x2")).ToArray());
        }
    }
}
