using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Client;
using NuGet.Commands;
using NuGet.Common;
using NuGet.ContentModel;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using NuGet.Repositories;
using NuGet.RuntimeModel;
using NuGet.Versioning;

namespace NuGetCompat
{
    public static class SupportedFrameworksProvider
    {
        public static IEnumerable<NuGetFramework> AllFrameworks { get; }
        public static IReadOnlyList<NuGetFramework> NonEquivalentFrameworks { get; }

        static SupportedFrameworksProvider()
        {
            var enumerator = new FrameworkEnumerator();
            var enumerated = enumerator.Enumerate(FrameworkEnumerationOptions.All ^ FrameworkEnumerationOptions.SpecialFrameworks);
            var expanded = enumerator.Expand(enumerated, FrameworkExpansionOptions.All);

            var allFrameworks = expanded.ToSet();

            // Don't allow frameworks that can't be rendered as short framework names.
            foreach (var framework in allFrameworks.ToList())
            {
                try
                {
                    framework.GetShortFolderName();
                }
                catch (FrameworkException)
                {
                    allFrameworks.Remove(framework);
                }
            }

            AllFrameworks = allFrameworks;
            NonEquivalentFrameworks = GetNonEquivalentFrameworks(AllFrameworks);
        }

        private static IReadOnlyList<NuGetFramework> GetNonEquivalentFrameworks(IEnumerable<NuGetFramework> frameworks)
        {
            // Group all frameworks with equivalents.
            var equivalentFrameworks = new Dictionary<NuGetFramework, HashSet<NuGetFramework>>();
            var distinctSets = new List<HashSet<NuGetFramework>>();
            var compat = DefaultCompatibilityProvider.Instance;
            var nonEquivalentSorter = new FrameworkPrecedenceSorter(DefaultFrameworkNameProvider.Instance, allEquivalent: false);
            var candidates = frameworks
                .Where(x => x.IsSpecificFramework)
                .OrderBy(x => x, nonEquivalentSorter)
                .ToList();
            for (var ai = 0; ai < candidates.Count - 1; ai++)
            {
                for (var bi = ai + 1; bi < candidates.Count; bi++)
                {
                    var a = candidates[ai];
                    var b = candidates[bi];

                    if (compat.IsCompatible(a, b) && compat.IsCompatible(b, a))
                    {
                        if (!equivalentFrameworks.TryGetValue(a, out var equivalentA))
                        {
                            equivalentA = new HashSet<NuGetFramework>();
                            equivalentFrameworks.Add(a, equivalentA);
                            distinctSets.Add(equivalentA);
                        }

                        if (!equivalentFrameworks.TryGetValue(b, out var equivalentB))
                        {
                            equivalentB = equivalentA;
                            equivalentFrameworks.Add(b, equivalentB);
                        }

                        if (!ReferenceEquals(equivalentA, equivalentB))
                        {
                            throw new InvalidOperationException($"The equivalent sets for {a} and {b} should be the same.");
                        }

                        equivalentB.Add(a);
                        equivalentA.Add(b);
                    }
                }
            }

            // Sort the sets so that a more user-friendly framework is used.
            var excludedEquivalents = new HashSet<NuGetFramework>();
            var equivalentSorter = new FrameworkPrecedenceSorter(DefaultFrameworkNameProvider.Instance, allEquivalent: true);
            foreach (var distinctSet in distinctSets)
            {
                var sorted = Sort(distinctSet, equivalentSorter);

                foreach (var excluded in sorted.Skip(1))
                {
                    excludedEquivalents.Add(excluded);
                }
            }

            // Exclude all equivalents but the first in the list (which is the more user-friendly).
            var output = new List<NuGetFramework>();
            foreach (var framework in frameworks)
            {
                if (excludedEquivalents.Contains(framework))
                {
                    continue;
                }

                output.Add(framework);
            }

            return Sort(output, nonEquivalentSorter);
        }

        private static List<NuGetFramework> Sort(IEnumerable<NuGetFramework> distinctSet, FrameworkPrecedenceSorter equivalentSorter)
        {
            return distinctSet
                .OrderBy(x => x, equivalentSorter)
                .ThenBy(x => x.Framework, StringComparer.OrdinalIgnoreCase) // Prefer A over Z in framework name.
                .ThenByDescending(x => x.Version) // Prefer higher versions since a higher version typically supports more things and this list is intended for project TFM.
                .ThenBy(x => (x.Profile ?? string.Empty).Length) // Prefer shorter profiles.
                .ThenBy(x => x.Profile, StringComparer.OrdinalIgnoreCase) // Prefer A over Z in profile.
                .ToList();
        }

        public static HashSet<NuGetFramework> SuggestedByNuspecReader(IReadOnlyList<string> files, string manifestPath, Func<Stream> getStream)
        {
            var packageReader = new InMemoryPackageReader(files, manifestPath, getStream);

            var frameworks = packageReader.GetSupportedFrameworks();

            return new HashSet<NuGetFramework>(frameworks);
        }

        public static HashSet<NuGetFramework> SuggestedByCompatibilityChecker(IReadOnlyList<string> files)
        {
            var compatibilityData = new CompatibilityChecker.CompatibilityData(
                files,
                targetLibrary: null,
                packageSpec: null);

            var restoreTargetGraph = RestoreTargetGraph.Create(
                new List<GraphNode<RemoteResolveResult>>(),
                new RemoteWalkContext(
                    new SourceCacheContext(),
                    NullLogger.Instance),
                NullLogger.Instance,
                NuGetFramework.AnyFramework);

            var frameworks = CompatibilityChecker.GetPackageFrameworks(
                compatibilityData,
                restoreTargetGraph);

            return new HashSet<NuGetFramework>(frameworks);
        }

        public static async Task<HashSet<NuGetFramework>> SupportedByCompatiblityChecker2Async(IReadOnlyList<string> files, NuspecReader nuspecReader, ILogger logger)
        {
            var compatible = new HashSet<NuGetFramework>();

            foreach (var framework in NonEquivalentFrameworks)
            {
                if (framework == NuGetFramework.AnyFramework)
                {
                }

                var libraryIdentity = new LibraryIdentity("TestLibrary", NuGetVersion.Parse("1.0.0"), LibraryType.Package);

                var project = new PackageSpec(new List<TargetFrameworkInformation>
                {
                    new TargetFrameworkInformation
                    {
                        FrameworkName = framework,
                        Dependencies = new List<LibraryDependency>
                        {
                            new LibraryDependency
                            {
                                LibraryRange = new LibraryRange(
                                    libraryIdentity.Name,
                                    VersionRange.All,
                                    LibraryDependencyTarget.Package),
                            },
                        }
                    }
                })
                {
                    Name = "TestProject",
                };

                var graphNode = new GraphNode<RemoteResolveResult>(new LibraryRange(
                    libraryIdentity.Name,
                    VersionRange.All,
                    LibraryDependencyTarget.Package));
                graphNode.Item = new GraphItem<RemoteResolveResult>(libraryIdentity)
                {
                    Data = new RemoteResolveResult
                    {
                        Match = new RemoteMatch
                        {
                            Library = libraryIdentity,
                        },
                    }
                };

                var restoreTargetGraph = RestoreTargetGraph.Create(
                    new List<GraphNode<RemoteResolveResult>> { graphNode },
                    new RemoteWalkContext(
                        new SourceCacheContext(),
                        logger),
                    logger,
                    framework);

                var targetGraphs = new List<RestoreTargetGraph> { restoreTargetGraph };

                var repository = new InMemoryNuGetV3Repository(new LocalPackageInfo(
                    libraryIdentity.Name,
                    libraryIdentity.Version,
                    path: null,
                    manifestPath: null,
                    zipPath: null,
                    sha512Path: null,
                    nuspec: new Lazy<NuspecReader>(() => nuspecReader),
                    files: new Lazy<IReadOnlyList<string>>(() => files),
                    sha512: new Lazy<string>(() => null),
                    runtimeGraph: null));

                var lockFileBuilder = new LockFileBuilder(
                    lockFileVersion: LockFileFormat.Version,
                    logger: logger,
                    includeFlagGraphs: new Dictionary<RestoreTargetGraph, Dictionary<string, LibraryIncludeFlags>>());

                var lockFile = lockFileBuilder.CreateLockFile(
                    previousLockFile: null,
                    project: project,
                    targetGraphs: targetGraphs,
                    localRepositories: new List<NuGetv3LocalRepository> { repository },
                    context: new RemoteWalkContext(
                        new SourceCacheContext(),
                        logger: logger));

                var includeFlags = new Dictionary<string, LibraryIncludeFlags>();

                var compatibilityChecker = new CompatibilityChecker(
                    new List<NuGetv3LocalRepository>(),
                    lockFile,
                    validateRuntimeAssets: false,
                    log: logger);

                var result = await compatibilityChecker.CheckAsync(
                    restoreTargetGraph,
                    includeFlags,
                    project);

                if (result.Success)
                {
                    compatible.Add(framework);
                }
            }


            return compatible;
        }

        public static HashSet<NuGetFramework> SupportedByCompatiblityChecker(IReadOnlyList<string> files, NuspecReader nuspecReader)
        {
            var contentItemCollection = new ContentItemCollection();
            contentItemCollection.Load(files);

            var runtimeGraph = new RuntimeGraph();
            var conventions = new ManagedCodeConventions(runtimeGraph);

            // TODO: need framework assemblies from .nuspec
            // TODO: content files might be matching too much... the product code does some pivoting and filtering.
            // TODO: only take .props and .targets matching the file name or _._ for build/buildbuildTransitive/buildMultiTargeting/buildCrossTargeting
            // TODO: support lib/contract
            // TODO: apply <references> filter from .nuspec

            // We won't even think about RIDs. It's impossible to determine a RID compatibility based on a single
            // package because runtime-specific libraries usually come from other packages.

            var allPatternSets = new Dictionary<string, PatternSet[]>
            {
                {
                    nameof(LockFileTargetLibrary.RuntimeAssemblies),
                    new[]
                    {
                        conventions.Patterns.RuntimeAssemblies,
                    }
                },
                {
                    nameof(LockFileTargetLibrary.CompileTimeAssemblies),
                    new[]
                    {
                        conventions.Patterns.CompileRefAssemblies,
                        conventions.Patterns.CompileLibAssemblies,
                    }
                },
                {
                    nameof(LockFileTargetLibrary.ContentFiles),
                    new[]
                    {
                        conventions.Patterns.ContentFiles,
                    }
                },
                {
                    nameof(LockFileTargetLibrary.ResourceAssemblies),
                    new[]
                    {
                        conventions.Patterns.ResourceAssemblies,
                    }
                },
                {
                    nameof(LockFileTargetLibrary.Build),
                    new[]
                    {
                        conventions.Patterns.MSBuildTransitiveFiles,
                        conventions.Patterns.MSBuildFiles,
                    }
                },
                {
                    nameof(LockFileTargetLibrary.BuildMultiTargeting),
                    new[]
                    {
                        conventions.Patterns.MSBuildMultiTargetingFiles,
                    }
                },
            };

            var frameworks = new HashSet<NuGetFramework>();
            foreach (var pair in allPatternSets)
            {
                var property = pair.Key;
                var patternSets = pair.Value;
                foreach (var patternSet in patternSets)
                {
                    var groups = contentItemCollection.FindItemGroups(patternSet);
                    foreach (var group in groups)
                    {
                        var tfm = (NuGetFramework)group.Properties[ManagedCodeConventions.PropertyNames.TargetFrameworkMoniker];
                        foreach (var item in group.Items)
                        {
                            frameworks.Add(tfm);
                        }
                    }
                }
            }

            return frameworks;
        }

        public static IEnumerable<NuGetFramework> SupportedByDuplicatingLogic(
            IReadOnlyList<string> files,
            NuspecReader nuspecReader,
            bool stdout = false)
        {
            var writer = stdout ? Console.Out : TextWriter.Null;

            writer.WriteLine(nuspecReader.GetId() + " " + nuspecReader.GetVersion().ToFullString());

            /// Based on <see cref="CompatibilityChecker"/> and <see cref="LockFileUtils"/>.
            // There are several caveats of this implementation:
            // 1. Does not consider transitive dependencies                          -- can lead to false positives
            // 2. Assumes package reference compatibility logic, not packages.config -- can lead to false positives and false negatives
            // 3. Assumes dependency package type                                    -- can lead to false positives
            // 4. Missing lib/contract back-compat check                             -- can lead to false positives
            // 5. Missing <references> filtering                                     -- can lead to false positives
            // 6. Considers framework assembly groups for package-based frameworks   -- can lead to false positives
            // 7. Does not consider runtime identifiers                              -- can lead to false positives

            var hasAssemblies = files.Any(p =>
                p.StartsWith("ref/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("lib/", StringComparison.OrdinalIgnoreCase));

            writer.WriteLine("  - Has assemblies: " + hasAssemblies);
            if (!hasAssemblies)
            {
                yield return NuGetFramework.AnyFramework;
            }

            var items = new ContentItemCollection();
            items.Load(files);

            var conventions = new ManagedCodeConventions(new RuntimeGraph());

            var patterns = new[]
            {
                conventions.Patterns.CompileRefAssemblies,
                conventions.Patterns.CompileLibAssemblies,
                conventions.Patterns.RuntimeAssemblies,
                conventions.Patterns.ContentFiles,
                conventions.Patterns.ResourceAssemblies,
            };

            var msbuildPatterns = new[]
            {
                conventions.Patterns.MSBuildTransitiveFiles,
                conventions.Patterns.MSBuildFiles,
                conventions.Patterns.MSBuildMultiTargetingFiles,
            };

            var groups = patterns
                .SelectMany(p => items.FindItemGroups(p));

            // Filter out MSBuild assets that don't match the package ID.
            var packageId = nuspecReader.GetId();
            var msbuildGroups = msbuildPatterns
                .SelectMany(p => items.FindItemGroups(p))
                .Where(g => HasBuildItemsForPackageId(g.Items, packageId));

            var frameworksFromAssets = groups
                .Concat(msbuildGroups)
                .SelectMany(p => p.Properties)
                .Where(pair => pair.Key == ManagedCodeConventions.PropertyNames.TargetFrameworkMoniker)
                .Select(pair => pair.Value)
                .Cast<NuGetFramework>()
                .Distinct();

            writer.WriteLine("  - Assets in the .nupkg:");
            foreach (var f in frameworksFromAssets)
            {
                writer.WriteLine($"    - {f.GetShortFolderName()}");
                yield return f;
            }

            var frameworksFromFrameworkAssemblyGroups = nuspecReader
                .GetFrameworkAssemblyGroups()
                .Select(g => g.TargetFramework)
                .Distinct();

            writer.WriteLine("  - <frameworkAssembly> in .nuspec:");
            foreach (var f in frameworksFromFrameworkAssemblyGroups)
            {
                writer.WriteLine($"    - {f.GetShortFolderName()}");
                yield return f;
            }

            var frameworksFromFrameworkReferenceGroups = nuspecReader
                .GetFrameworkRefGroups()
                .Select(g => g.TargetFramework)
                .Distinct();

            writer.WriteLine("  - <frameworkReference> in .nuspec:");
            foreach (var f in frameworksFromFrameworkReferenceGroups)
            {
                writer.WriteLine($"    - {f.GetShortFolderName()}");
                yield return f;
            }
        }

        private static bool HasBuildItemsForPackageId(IEnumerable<ContentItem> items, string packageId)
        {
            foreach (var item in items)
            {
                var fileName = Path.GetFileName(item.Path);
                if (fileName == PackagingCoreConstants.EmptyFolder)
                {
                    return true;
                }

                if ($"{packageId}.props".Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if ($"{packageId}.targets".Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
