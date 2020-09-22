using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NuGet.Frameworks.Generator
{
    [Generator]
    public class AllFrameworksSourceGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            var enumerator = new FrameworkEnumerator();
            var enumerated = enumerator.Enumerate(FrameworkEnumerationOptions.All ^ FrameworkEnumerationOptions.SpecialFrameworks);
            var expanded = enumerator.Expand(enumerated, FrameworkExpansionOptions.All);

            var allFrameworks = new HashSet<NuGetFramework>(expanded);

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

            var nonEquivalentFrameworks = GetNonEquivalentFrameworks(allFrameworks);

            var sourceBuilder = new StringBuilder();

            sourceBuilder.Append($@"
using System;
using System.Collections.Generic;
using NuGet.Frameworks;

namespace NuGet.Frameworks
{{
    public static class EnumeratedFrameworks
    {{
        public static IEnumerable<NuGetFramework> All => _all;
        public static IEnumerable<NuGetFramework> NonEquivalent => _nonEquivalent;
");

            AppendFrameworkListField("_all", allFrameworks, sourceBuilder);
            AppendFrameworkListField("_nonEquivalent", nonEquivalentFrameworks, sourceBuilder);

            sourceBuilder.Append($@"
    }}
}}");

            context.AddSource("GeneratedEnumeratedFrameworks", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
        }

        private static void AppendFrameworkListField(string name, IEnumerable<NuGetFramework> frameworks, StringBuilder sourceBuilder)
        {
            sourceBuilder.AppendFormat("        private static readonly IReadOnlyList<NuGetFramework> {0} = new List<NuGetFramework>", name);
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("        {");
            AppendFrameworkListItems(frameworks, sourceBuilder);
            sourceBuilder.AppendLine("        };");
        }

        private static void AppendFrameworkListItems(IEnumerable<NuGetFramework> frameworks, StringBuilder sourceBuilder)
        {
            foreach (var framework in frameworks)
            {
                sourceBuilder.Append("            ");
                sourceBuilder.AppendFormat("new NuGetFramework(\"{0}\", ", framework.Framework);
                AppendVersion(framework.Version, sourceBuilder);
                
                if (framework.HasPlatform)
                {
                    sourceBuilder.AppendFormat(", \"{0}\", ", framework.Platform);
                    AppendVersion(framework.PlatformVersion, sourceBuilder);
                }
                else if (framework.HasProfile)
                {
                    sourceBuilder.AppendFormat(", \"{0}\"", framework.Profile);
                }

                sourceBuilder.AppendLine("),");
            }
        }

        private static void AppendVersion(Version version, StringBuilder sourceBuilder)
        {
            sourceBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "new Version({0}, {1}, {2}, {3})",
                version.Major,
                version.Minor,
                version.Build,
                version.Revision);
        }

        public void Initialize(GeneratorInitializationContext context)
        {
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
    }

}
