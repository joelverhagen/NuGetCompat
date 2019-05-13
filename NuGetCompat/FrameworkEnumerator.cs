using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NuGet.Frameworks;

namespace NuGetCompat
{
    public class FrameworkEnumerator
    {
        public IEnumerable<NuGetFramework> Expand(IEnumerable<NuGetFramework> frameworks, FrameworkExpansionOptions options)
        {
            var existing = new HashSet<NuGetFramework>();

            foreach (var data in frameworks)
            {
                var originalAdded = AddFramework(existing, data);
                if (originalAdded != null)
                {
                    yield return originalAdded;
                }

                if (options.HasFlag(FrameworkExpansionOptions.RoundTripDotNetFrameworkName))
                {
                    foreach (var added in ExpandByRoundTrippingDotNetFrameworkName(existing, data))
                    {
                        yield return added;
                    }
                }

                if (options.HasFlag(FrameworkExpansionOptions.RoundTripShortFolderName))
                {
                    foreach (var added in ExpandByRoundTrippingShortFolderName(existing, data))
                    {
                        yield return added;
                    }
                }

                if (options.HasFlag(FrameworkExpansionOptions.FrameworkExpander))
                {
                    var expander = new FrameworkExpander();

                    foreach (var added in ExpandByUsingFrameworkExpander(existing, data, expander))
                    {
                        yield return added;
                    }
                }

                if (options.HasFlag(FrameworkExpansionOptions.MinimumVersion))
                {
                    foreach (var added in ExpandByUsingMinimumVersion(existing, data))
                    {
                        yield return added;
                    }
                }

                if (options.HasFlag(FrameworkExpansionOptions.RemoveProfile))
                {
                    foreach (var added in ExpandByRemovingProfile(existing, data))
                    {
                        yield return added;
                    }
                }
            }
        }

        public IEnumerable<NuGetFramework> Enumerate(FrameworkEnumerationOptions options)
        {
            var existing = new HashSet<NuGetFramework>();

            if (options.HasFlag(FrameworkEnumerationOptions.FrameworkNameProvider))
            {
                foreach (var added in AddDefaultFrameworkNameProvider(existing))
                {
                    yield return added;
                }
            }

            if (options.HasFlag(FrameworkEnumerationOptions.CommonFrameworks))
            {
                foreach (var added in AddCommonFrameworks(existing))
                {
                    yield return added;
                }
            }

            if (options.HasFlag(FrameworkEnumerationOptions.FrameworkMappings))
            {
                foreach (var added in AddDefaultFrameworkMappings(existing))
                {
                    yield return added;
                }
            }

            if (options.HasFlag(FrameworkEnumerationOptions.PortableFrameworkMappings))
            {
                foreach (var added in AddDefaultPortableFrameworkMappings(existing))
                {
                    yield return added;
                }
            }

            if (options.HasFlag(FrameworkEnumerationOptions.SpecialFrameworks))
            {
                foreach (var added in AddSpecialFrameworks(existing))
                {
                    yield return added;
                }
            }
        }

        private IEnumerable<NuGetFramework> AddSpecialFrameworks(HashSet<NuGetFramework> existing)
        {
            var specialFrameworks = new[]
            {
                NuGetFramework.AgnosticFramework,
                NuGetFramework.AnyFramework,
                NuGetFramework.UnsupportedFramework
            };

            return AddFrameworks(existing, specialFrameworks);
        }

        private static IEnumerable<NuGetFramework> AddDefaultFrameworkNameProvider(HashSet<NuGetFramework> existing)
        {
            var frameworkNameProvider = DefaultFrameworkNameProvider.Instance;

            var compatibilityCandidates = frameworkNameProvider
                .GetCompatibleCandidates();

            return AddFrameworks(existing, compatibilityCandidates);
        }

        private static IEnumerable<NuGetFramework> AddDefaultPortableFrameworkMappings(HashSet<NuGetFramework> existing)
        {
            var portableFrameworkMappings = DefaultPortableFrameworkMappings.Instance;

            var profileFrameworks = portableFrameworkMappings
                .ProfileFrameworks
                .SelectMany(x => x.Value);

            foreach (var added in AddFrameworks(existing, profileFrameworks))
            {
                yield return added;
            }

            var profileFrameworksNumbers = portableFrameworkMappings
                .ProfileFrameworks
                .Select(x => GetPortableFramework(x.Key));

            foreach (var added in AddFrameworks(existing, profileFrameworksNumbers))
            {
                yield return added;
            }

            var profileOptionalFrameworks = portableFrameworkMappings
                .ProfileOptionalFrameworks
                .SelectMany(x => x.Value);

            foreach (var added in AddFrameworks(existing, profileOptionalFrameworks))
            {
                yield return added;
            }

            var profileOptionalFrameworksNumbers = portableFrameworkMappings
                .ProfileOptionalFrameworks
                .Select(x => GetPortableFramework(x.Key));

            foreach (var added in AddFrameworks(existing, profileOptionalFrameworksNumbers))
            {
                yield return added;
            }

            var compatibilityMappings = portableFrameworkMappings
                .CompatibilityMappings
                .SelectMany(x => new[] { x.Value.Min, x.Value.Max });

            foreach (var added in AddFrameworks(existing, compatibilityMappings))
            {
                yield return added;
            }

            var compatibilityMappingsNumbers = portableFrameworkMappings
                .CompatibilityMappings
                .Select(x => GetPortableFramework(x.Key));

            foreach (var added in AddFrameworks(existing, compatibilityMappingsNumbers))
            {
                yield return added;
            }
        }

        private static IEnumerable<NuGetFramework> AddDefaultFrameworkMappings(HashSet<NuGetFramework> existing)
        {
            var frameworkMappings = DefaultFrameworkMappings.Instance;

            var equivalentFrameworks = frameworkMappings
                .EquivalentFrameworks
                .SelectMany(x => new[] { x.Key, x.Value });

            foreach (var added in AddFrameworks(existing, equivalentFrameworks))
            {
                yield return added;
            }

            var compatibilityMappings = frameworkMappings
                .CompatibilityMappings
                .SelectMany(x => (new[]
                {
                    x.SupportedFrameworkRange.Min,
                    x.SupportedFrameworkRange.Max,
                    x.TargetFrameworkRange.Min,
                    x.TargetFrameworkRange.Max
                }));

            foreach (var added in AddFrameworks(existing, compatibilityMappings))
            {
                yield return added;
            }

            var shortNameReplacements = frameworkMappings
                .ShortNameReplacements
                .SelectMany(x => new[] { x.Key, x.Value });

            foreach (var added in AddFrameworks(existing, shortNameReplacements))
            {
                yield return added;
            }

            var fullNameReplacements = frameworkMappings
                .FullNameReplacements
                .SelectMany(x => new[] { x.Key, x.Value });

            foreach (var added in AddFrameworks(existing, fullNameReplacements))
            {
                yield return added;
            }
        }

        private static IEnumerable<NuGetFramework> AddCommonFrameworks(HashSet<NuGetFramework> existing)
        {
            var commonFrameworksType = typeof(FrameworkConstants.CommonFrameworks);

            var commonFrameworks = commonFrameworksType
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(x => x.FieldType == typeof(NuGetFramework))
                .Select(x => x.GetValue(null))
                .Cast<NuGetFramework>();

            return AddFrameworks(existing, commonFrameworks);
        }

        private static IEnumerable<NuGetFramework> ExpandByRoundTrippingShortFolderName(
            HashSet<NuGetFramework> existing,
            NuGetFramework data)
        {
            var shortFolderName = data.GetShortFolderName();
            var roundTrip = NuGetFramework.ParseFolder(shortFolderName);

            var added = AddFramework(existing, roundTrip);
            if (added != null)
            {
                yield return added;
            }
        }

        private static IEnumerable<NuGetFramework> ExpandByRoundTrippingDotNetFrameworkName(
            HashSet<NuGetFramework> existing,
            NuGetFramework data)
        {
            var dotNetFrameworkName = data.DotNetFrameworkName;
            var roundTrip = NuGetFramework.Parse(dotNetFrameworkName);

            var added = AddFramework(existing, roundTrip);
            if (added != null)
            {
                yield return added;
            }
        }

        private static IEnumerable<NuGetFramework> ExpandByUsingFrameworkExpander(
            HashSet<NuGetFramework> existing,
            NuGetFramework data,
            FrameworkExpander expander)
        {
            var expanded = expander.Expand(data);

            if (expanded.Any())
            {
                foreach (var added in AddFrameworks(existing, expanded))
                {
                    yield return added;
                }
            }
        }

        private static IEnumerable<NuGetFramework> ExpandByUsingMinimumVersion(
            HashSet<NuGetFramework> existing,
            NuGetFramework data)
        {
            var minVersion = new NuGetFramework(data.Framework, new Version(0, 0), data.Profile);
            var added = AddFramework(existing, minVersion);
            if (added != null)
            {
                yield return added;
            }
        }

        private static IEnumerable<NuGetFramework> ExpandByRemovingProfile(
            HashSet<NuGetFramework> existing,
            NuGetFramework data)
        {
            var noProfile = new NuGetFramework(data.Framework, data.Version, frameworkProfile: null);
            var added = AddFramework(existing, noProfile);
            if (added != null)
            {
                yield return added;
            }
        }

        private static NuGetFramework GetPortableFramework(int profileNumber)
        {
            return new NuGetFramework(
                FrameworkConstants.FrameworkIdentifiers.Portable,
                FrameworkConstants.EmptyVersion,
                FrameworkNameHelpers.GetPortableProfileNumberString(profileNumber));
        }

        private static IEnumerable<NuGetFramework> AddFrameworks(HashSet<NuGetFramework> existing, IEnumerable<NuGetFramework> toAdd)
        {
            foreach (var data in toAdd)
            {
                var added = AddFramework(existing, data);
                if (added != null)
                {
                    yield return added;
                }
            }
        }

        private static NuGetFramework AddFramework(HashSet<NuGetFramework> existing, NuGetFramework data)
        {
            if (data.Version == FrameworkConstants.MaxVersion)
            {
                return null;
            }

            if (!existing.Add(data))
            {
                return null;
            }

            return data;
        }
    }
}
