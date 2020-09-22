using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public static IReadOnlyList<NuGetFramework> SupportedByNuspecReader(IReadOnlyList<string> files, Func<Stream> getStream)
        {
            var packageReader = new InMemoryPackageReader(files, getStream);
            return packageReader.GetSupportedFrameworks().ToList();
        }

        public static IReadOnlyList<NuGetFramework> SuggestedByNU1202(IReadOnlyList<string> files)
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

            return CompatibilityChecker.GetPackageFrameworks(
                compatibilityData,
                restoreTargetGraph);
        }

        public static async Task<HashSet<NuGetFramework>> SupportedByFrameworkEnumerationAsync(IReadOnlyList<string> files, NuspecReader nuspecReader, ILogger logger)
        {
            var compatible = new HashSet<NuGetFramework>();

            foreach (var framework in NuGet.Frameworks.EnumeratedFrameworks.NonEquivalent)
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

        public static HashSet<NuGetFramework> SupportedByPatternSets(IReadOnlyList<string> files)
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

        public static IEnumerable<NuGetFramework> SupportedByDuplicatedLogic(
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
