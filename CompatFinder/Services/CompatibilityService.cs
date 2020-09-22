using Knapcode.MiniZip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using NuGetCompat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace CompatFinder.Services
{
    public class ResultWithDuration<T>
    {
        public ResultWithDuration(T result, TimeSpan duration)
        {
            Result = result;
            Duration = duration;
        }

        public T Result { get; }
        public TimeSpan Duration { get; }
    }

    public enum CompatibilityResultType
    {
        Ok,
        NotFound,
    }

    public class SupportedFrameworks
    {
        public SupportedFrameworks(
            ResultWithDuration<IReadOnlyList<NuGetFramework>> nuspecReader,
            ResultWithDuration<IReadOnlyList<NuGetFramework>> nU1202,
            ResultWithDuration<IReadOnlyList<NuGetFramework>> patternSets,
            ResultWithDuration<IReadOnlyList<NuGetFramework>> frameworkEnumeration,
            ResultWithDuration<IReadOnlyList<NuGetFramework>> duplicatedLogic)
        {
            NuspecReader = nuspecReader;
            NU1202 = nU1202;
            PatternSets = patternSets;
            FrameworkEnumeration = frameworkEnumeration;
            DuplicatedLogic = duplicatedLogic;
        }

        public ResultWithDuration<IReadOnlyList<NuGetFramework>> NuspecReader { get; }
        public ResultWithDuration<IReadOnlyList<NuGetFramework>> NU1202 { get; }
        public ResultWithDuration<IReadOnlyList<NuGetFramework>> PatternSets { get; }
        public ResultWithDuration<IReadOnlyList<NuGetFramework>> FrameworkEnumeration { get; }
        public ResultWithDuration<IReadOnlyList<NuGetFramework>> DuplicatedLogic { get; }
    }

    public class CompatibilityResult
    {
        private CompatibilityResult(
            CompatibilityResultType type,
            ResultWithDuration<List<string>> files,
            ResultWithDuration<NuspecReader> nuspecReader,
            SupportedFrameworks supportedFrameworks)
        {
            Type = type;
            Files = files;
            NuspecReader = nuspecReader;
            SupportedFrameworks = supportedFrameworks;
        }

        public static CompatibilityResult NotFound()
        {
            return new CompatibilityResult(CompatibilityResultType.NotFound, files: null, nuspecReader: null, supportedFrameworks: null);
        }

        public static CompatibilityResult Ok(
            ResultWithDuration<List<string>> files,
            ResultWithDuration<NuspecReader> nuspecReader,
            SupportedFrameworks supportedFrameworks)
        {
            return new CompatibilityResult(CompatibilityResultType.Ok, files, nuspecReader, supportedFrameworks);
        }

        public CompatibilityResultType Type { get; }
        public ResultWithDuration<List<string>> Files { get; }
        public ResultWithDuration<NuspecReader> NuspecReader { get; }
        public SupportedFrameworks SupportedFrameworks { get; }
    }

    public class CompatibilityService
    {
        /// <summary>
        /// TODO: use the service index to discover.
        /// </summary>
        private const string PackageBaseAddress = "https://api.nuget.org/v3-flatcontainer/";

        public async Task<CompatibilityResult> GetCompatibilityAsync(string id, NuGetVersion version, bool allowEnumerate)
        {
            var lowerId = id.ToLowerInvariant();
            var lowerVersion = version.ToNormalizedString().ToLowerInvariant();
            using HttpClient httpClient = new HttpClient();

            // Get the list of files in the package.
            var files = await ExecuteAsync(() => GetFilesAsync(httpClient, lowerId, lowerVersion));
            if (files.Result == null)
            {
                return CompatibilityResult.NotFound();
            }

            // Get and parse the .nuspec.
            var stopwatch = Stopwatch.StartNew();
            using var nuspecStream = await GetNuspecStreamAsync(httpClient, lowerId, lowerVersion);
            var nuspecDocument = LoadDocument(nuspecStream);
            var nuspecReader = new NuspecReader(nuspecDocument);
            var nuspecReaderDuration = stopwatch.Elapsed;

            // Determine the supported frameworks using various approaches.
            var suggestedByNuspecReader = Execute(
                () => SupportedFrameworksProvider.SupportedByNuspecReader(
                    files.Result,
                    () =>
                    {
                        var memoryStream = new MemoryStream();
                        nuspecStream.Position = 0;
                        nuspecStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;
                        return memoryStream;
                    }));

            var suggestedByNU1202 = Execute(
                () => SupportedFrameworksProvider.SuggestedByNU1202(files.Result));

            var supportedByPatternSets = Execute<IReadOnlyList<NuGetFramework>>(
                () => SupportedFrameworksProvider.SupportedByPatternSets(files.Result).ToList());

            var supportedByFrameworkEnumeration = await ExecuteAsync<IReadOnlyList<NuGetFramework>>(
                async () =>
                {
                    if (allowEnumerate)
                    {
                        var result = await SupportedFrameworksProvider.SupportedByFrameworkEnumerationAsync(
                            files.Result,
                        nuspecReader,
                        NuGet.Common.NullLogger.Instance);
                        return result.ToList();
                    }
                    else
                    {
                        return Array.Empty<NuGetFramework>();
                    }
                });

            var supportedByDuplicatedLogic = Execute<IReadOnlyList<NuGetFramework>>(
                () => SupportedFrameworksProvider.SupportedByDuplicatedLogic(files.Result, nuspecReader).ToList());

            var supportedFrameworks = new SupportedFrameworks(
                suggestedByNuspecReader,
                suggestedByNU1202,
                supportedByPatternSets,
                supportedByFrameworkEnumeration,
                supportedByDuplicatedLogic);

            return CompatibilityResult.Ok(
                files,
                new ResultWithDuration<NuspecReader>(nuspecReader, nuspecReaderDuration),
                supportedFrameworks);
        }

        private static async Task<Stream> GetNuspecStreamAsync(HttpClient httpClient, string id, string version)
        {
            var url = $"{PackageBaseAddress}{id}/{version}/{id}.nuspec";
            return await httpClient.GetStreamAsync(url);
        }

        private static async Task<List<string>> GetFilesAsync(HttpClient httpClient, string id, string version)
        {
            var url = $"{PackageBaseAddress}{id}/{version}/{id}.{version}.nupkg";
            var httpZipProvider = new HttpZipProvider(httpClient);
            httpZipProvider.RequireAcceptRanges = false;
            httpZipProvider.RequireContentRange = false;

            try
            {
                using var zipDirectoryReader = await httpZipProvider.GetReaderAsync(new Uri(url));
                var zipDirectory = await zipDirectoryReader.ReadAsync();

                return zipDirectory
                    .Entries
                    .Select(x => x.GetName())
                    .ToList();
            }
            catch (MiniZipHttpStatusCodeException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw;
            }
        }

        private static XDocument LoadDocument(Stream stream)
        {
            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };

            settings.XmlResolver = null;

            using (var streamReader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
            using (var xmlReader = XmlReader.Create(streamReader, settings))
            {
                return XDocument.Load(xmlReader, LoadOptions.None);
            }
        }

        private ResultWithDuration<T> Execute<T>(Func<T> act)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = act();
            return new ResultWithDuration<T>(result, stopwatch.Elapsed);
        }

        private async Task<ResultWithDuration<T>> ExecuteAsync<T>(Func<Task<T>> actAsync)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await actAsync();
            return new ResultWithDuration<T>(result, stopwatch.Elapsed);
        }
    }
}
