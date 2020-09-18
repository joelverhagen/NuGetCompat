using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Repositories;
using NuGet.Versioning;

namespace NuGetCompat
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        static async Task MainAsync(CancellationToken cancellationToken)
        {
            var repository = Repository.Factory.GetCoreV3(@"C:\Users\jver\.nuget\packages");
            var listResource = await repository.GetResourceAsync<ListResource>();

            var list = await listResource.ListAsync(
                searchTerm: null,
                prerelease: true,
                allVersions: true,
                includeDelisted: true,
                log: NullLogger.Instance,
                token: CancellationToken.None);

            var enumerator = list.GetEnumeratorAsync();
            while (await enumerator.MoveNextAsync())
            {
                var package = GetPackageFromLocalRepository(
                    @"C:\Users\jver\.nuget\packages",
                    enumerator.Current.Identity.Id,
                    enumerator.Current.Identity.Version);

                var fromNuspecReader = SupportedFrameworksProvider.SuggestedByNuspecReader(
                    package.Files,
                    Path.GetFileName(package.ManifestPath),
                    () => File.OpenRead(package.ManifestPath));
                fromNuspecReader = ReduceFrameworks(fromNuspecReader);

                var suggestedByRestore = SupportedFrameworksProvider.SuggestedByCompatibilityChecker(package.Files);
                suggestedByRestore = ReduceFrameworks(suggestedByRestore);

                var supportedByRestore = SupportedFrameworksProvider.SupportedByCompatiblityChecker(package.Files, package.Nuspec);
                supportedByRestore = ReduceFrameworks(supportedByRestore);

                var supportedByRestore2 = await SupportedFrameworksProvider.SupportedByCompatiblityChecker2Async(
                    package.Files,
                    new NuspecReader(File.OpenRead(package.ManifestPath)),
                    NullLogger.Instance);
                supportedByRestore2 = ReduceFrameworks(supportedByRestore2);

                Console.WriteLine(enumerator.Current.Identity);
                DumpFrameworks(nameof(fromNuspecReader), fromNuspecReader);
                DumpFrameworks(nameof(suggestedByRestore), suggestedByRestore);
                DumpFrameworks(nameof(supportedByRestore), supportedByRestore);
                DumpFrameworks(nameof(supportedByRestore2), supportedByRestore2);
                Console.WriteLine();

                break;
            }
        }

        private static void DumpFrameworks(string name, HashSet<NuGetFramework> frameworks)
        {
            Console.WriteLine($"From {name}:");
            foreach (var framework in frameworks)
            {
                Console.WriteLine($"  {framework.GetShortFolderName()}");
            }
        }

        private static HashSet<NuGetFramework> ReduceFrameworks(HashSet<NuGetFramework> frameworks)
        {
            var specificGroups = frameworks.ToLookup(x => x.IsSpecificFramework);
            var reducer = new FrameworkReducer();
            var reducedSpecific = reducer.ReduceDownwards(specificGroups[true]);

            var set = reducedSpecific
                .Concat(specificGroups[false].Distinct())
                .OrderBy(x => x, new NuGetFrameworkSorter())
                .ToSet();

            return set;
        }

        private static NuGet.Repositories.LocalPackageInfo GetPackageFromLocalRepository(string path, string id, NuGetVersion version)
        {
            var repository = new NuGetv3LocalRepository(path);
            using (var sourceCacheContext = new SourceCacheContext())
            {
                var package = repository.FindPackage(id, version);
                if (package == null)
                {
                    throw new InvalidOperationException("The requested package does not exist in the local repository.");
                }

                return package;
            }
        }
    }
}
