using Knapcode.MiniZip;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using NuGetCompat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace CompatFinder.Services
{
    public enum CompatibilityResultType
    {
        Ok,
        NotFound,
    }

    public class SupportedFrameworks
    {
        public SupportedFrameworks(
            IReadOnlyList<NuGetFramework> nuspecReader,
            IReadOnlyList<NuGetFramework> nU1202,
            IReadOnlyList<NuGetFramework> patternSets,
            IReadOnlyList<NuGetFramework> frameworkEnumeration,
            IReadOnlyList<NuGetFramework> duplicatedLogic)
        {
            NuspecReader = nuspecReader;
            NU1202 = nU1202;
            PatternSets = patternSets;
            FrameworkEnumeration = frameworkEnumeration;
            DuplicatedLogic = duplicatedLogic;
        }

        public IReadOnlyList<NuGetFramework> NuspecReader { get; }
        public IReadOnlyList<NuGetFramework> NU1202 { get; }
        public IReadOnlyList<NuGetFramework> PatternSets { get; }
        public IReadOnlyList<NuGetFramework> FrameworkEnumeration { get; }
        public IReadOnlyList<NuGetFramework> DuplicatedLogic { get; }
    }

    public class CompatibilityResult
    {
        private CompatibilityResult(
            CompatibilityResultType type,
            IReadOnlyList<string> files,
            NuspecReader nuspecReader,
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

        public static CompatibilityResult Ok(IReadOnlyList<string> files, NuspecReader nuspecReader, SupportedFrameworks supportedFrameworks)
        {
            return new CompatibilityResult(CompatibilityResultType.Ok, files, nuspecReader, supportedFrameworks);
        }

        public CompatibilityResultType Type { get; }
        public IReadOnlyList<string> Files { get; }
        public NuspecReader NuspecReader { get; }
        public SupportedFrameworks SupportedFrameworks { get; }
    }

    public class CompatibilityService
    {
        /// <summary>
        /// TODO: use the service index to discover.
        /// </summary>
        private const string PackageBaseAddress = "https://api.nuget.org/v3-flatcontainer/";

        public async Task<CompatibilityResult> GetCompatibilityAsync(string id, NuGetVersion version)
        {
            var lowerId = id.ToLowerInvariant();
            var lowerVersion = version.ToNormalizedString().ToLowerInvariant();
            using HttpClient httpClient = new HttpClient();

            // Get the list of files in the package.
            var files = await GetFilesAsync(httpClient, lowerId, lowerVersion);
            if (files == null)
            {
                return CompatibilityResult.NotFound();
            }

            // Get and parse the .nuspec.
            using var nuspecStream = await GetNuspecStreamAsync(httpClient, lowerId, lowerVersion);
            var nuspecDocument = LoadDocument(nuspecStream);
            var nuspecReader = new NuspecReader(nuspecDocument);

            // Determine the supported frameworks using various approaches.
            /*
            var suggestedByNuspecReader = SupportedFrameworksProvider.SupportedByNuspecReader(
                files,
                () => nuspecStream);
            */
            var suggestedByNuspecReader = new List<NuGetFramework>();

            var suggestedByNU1202 = SupportedFrameworksProvider.SuggestedByNU1202(files);

            var supportedByPatternSets = SupportedFrameworksProvider.SupportedByPatternSets(files);

            var supportedByFrameworkEnumeration = await SupportedFrameworksProvider.SupportedByFrameworkEnumerationAsync(
                files,
                nuspecReader,
                NuGet.Common.NullLogger.Instance);

            var supportedByDuplicatedLogic = SupportedFrameworksProvider.SupportedByDuplicatedLogic(
                files,
                nuspecReader);

            var supportedFrameworks = new SupportedFrameworks(
                suggestedByNuspecReader.ToList(),
                suggestedByNU1202.ToList(),
                supportedByPatternSets.ToList(),
                supportedByFrameworkEnumeration.ToList(),
                supportedByDuplicatedLogic.ToList());

            return CompatibilityResult.Ok(files, nuspecReader, supportedFrameworks);
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

            // This is intentionally separate from the object initializer so that FXCop can see it.
            settings.XmlResolver = null;

            using (var streamReader = new StreamReader(stream))
            using (var xmlReader = XmlReader.Create(streamReader, settings))
            {
                return XDocument.Load(xmlReader, LoadOptions.None);
            }
        }
    }
}
