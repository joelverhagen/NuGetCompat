using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;

namespace NuGetCompat
{
    public class InMemoryPackageReader : PackageReaderBase
    {
        private readonly IReadOnlyList<string> _files;
        private readonly string _manifestPath;
        private readonly Func<Stream> _getManifestStream;

        public InMemoryPackageReader(IReadOnlyList<string> files, string manifestPath, Func<Stream> getNuspecStream) : base(DefaultFrameworkNameProvider.Instance)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _manifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
            _getManifestStream = getNuspecStream ?? throw new ArgumentNullException(nameof(getNuspecStream));
        }

        public override bool CanVerifySignedPackages(SignedPackageVerifierSettings verifierSettings)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<string> CopyFiles(string destination, IEnumerable<string> packageFiles, ExtractPackageFileDelegate extractFile, ILogger logger, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public override Task<byte[]> GetArchiveHashAsync(HashAlgorithmName hashAlgorithm, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public override string GetContentHash(CancellationToken token, Func<string> GetUnsignedPackageHash = null)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<string> GetFiles()
        {
            return _files;
        }

        public override IEnumerable<string> GetFiles(string folder)
        {
            return _files.Where(x => x.StartsWith(folder + "/"));
        }

        public override Task<PrimarySignature> GetPrimarySignatureAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public override Stream GetStream(string path)
        {
            if (path == _manifestPath)
            {
                return _getManifestStream();
            }

            throw new NotImplementedException();
        }

        public override Task<bool> IsSignedAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public override Task ValidateIntegrityAsync(SignatureContent signatureContent, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            throw new NotImplementedException();
        }
    }
}
