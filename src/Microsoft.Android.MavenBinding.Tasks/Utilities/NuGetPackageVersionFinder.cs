using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MavenNet.Models;
using NuGet.ProjectModel;

namespace Prototype.Android.MavenBinding.Tasks
{
	public class NuGetPackageVersionFinder
	{
		LockFile lock_file;
		Dictionary<string, Artifact> cache = new Dictionary<string, Artifact> ();
		Regex tag = new Regex ("artifact_versioned=(?<GroupId>.+)?:(?<ArtifactId>.+?):(?<Version>.+)\\s?", RegexOptions.Compiled);
		Regex tag2 = new Regex ("artifact=(?<GroupId>.+)?:(?<ArtifactId>.+?):(?<Version>.+)\\s?", RegexOptions.Compiled);

		NuGetPackageVersionFinder (LockFile lockFile)
		{
			lock_file = lockFile;
		}

		public static NuGetPackageVersionFinder? Create (string filename, LogWrapper log)
		{
			try {
				var lock_file_format = new LockFileFormat ();
				var lock_file = lock_file_format.Read (filename);
				return new NuGetPackageVersionFinder (lock_file);
			} catch (Exception e) {
				log.LogError (e.Message);
				return null;
			}
		}

		/// <summary>
		/// Scans the nuget package for native android library meta-data in the Tags field.
		/// </summary>
		/// <param name="library">The NuGet Package Name</param>
		/// <param name="version">NuGet package version</param>
		/// <param name="log"></param>
		/// <returns></returns>
		public IEnumerable<Artifact> GetJavaInformation (string library, string version, LogWrapper log)
		{
			log.LogMessage ($"===== entering {nameof(GetJavaInformation)}(library: {library}, version: {version}) =====");
			// Check if we already have this one in the cache
			var dictionary_key = $"{library.ToLowerInvariant ()}:{version}";
			var artifacts = new List<Artifact> ();

			if (cache.TryGetValue (dictionary_key, out var artifact))
				return artifacts;

			// Find the LockFileLibrary
			var nuget = lock_file.GetLibrary (library, new NuGet.Versioning.NuGetVersion (version));

			if (nuget is null) {
				log.LogError ("Could not find NuGet package '{0}' version '{1}' in lock file. Ensure NuGet Restore has run since this <PackageReference> was added.", library, version);
				return artifacts;
			}

			foreach (var path in lock_file.PackageFolders) {
				log.LogMessage ($"\"===== folder: {path.Path} =====");
				artifacts.AddRange(CheckFilePath (path.Path, nuget));
			}

			log.LogMessage ($"===== Found {artifacts.Count ()} java libs in pack {dictionary_key} =====");
			foreach (var art in artifacts) {
				log.LogMessage ($"===== java lib: {art.Id} =====");
				cache [dictionary_key] = art;
			}

			return artifacts;
		}

		IEnumerable<Artifact> CheckFilePath (string nugetPackagePath, LockFileLibrary package)
		{
			// Check NuGet tags
			var nuspec = package.Files.FirstOrDefault (f => f.EndsWith (".nuspec", StringComparison.OrdinalIgnoreCase));

			if (nuspec is null)
				return Enumerable.Empty<Artifact> ();

			nuspec = Path.Combine (nugetPackagePath, package.Path, nuspec);

			if (!File.Exists (nuspec))
				return Enumerable.Empty<Artifact> ();

			var reader = new NuGet.Packaging.NuspecReader (nuspec);
			// don't assume only one native artifact tagged...
			var tags = reader.GetTags ().Split(' ');

			return tags.Select (_ => {

				// Try the first tag format
				var match = tag.Match (_);

				// Try the second tag format
				if (!match.Success)
					match = tag2.Match (_);

				if (!match.Success)
					return null;

				// TODO: Define a well-known file that can be included in the package like "java-package.txt"

				return new Artifact (match.Groups ["ArtifactId"].Value, match.Groups ["GroupId"].Value, match.Groups ["Version"].Value);
			})
			// filter out any null items (metadata tags that don't match our regex).
			.Where (_ => _ != null)
			.Cast<Artifact>();
		}
	}
}
