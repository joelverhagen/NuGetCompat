using System.IO;
using NuGet.Repositories;
using NuGet.Versioning;

namespace NuGetCompat
{
    public class InMemoryNuGetV3Repository : NuGetv3LocalRepository
    {
        private readonly LocalPackageInfo _localPackageInfo;

        public InMemoryNuGetV3Repository(LocalPackageInfo localPackageInfo) : base(Directory.GetCurrentDirectory())
        {
            _localPackageInfo = localPackageInfo;
        }

        public override LocalPackageInfo FindPackage(string packageId, NuGetVersion version)
        {
            return _localPackageInfo;
        }
    }
}
