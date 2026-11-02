using Jobmate.Domain.Exceptions;
using Jobmate.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Jobmate.Api.Media;

/// <summary>Which family a stored file belongs to. Decides the folder and who may read it.</summary>
public enum MediaKind
{
    /// <summary>Avatars and anything else shown on a profile screen.</summary>
    ProfileImage,

    /// <summary>
    /// Identity and skill documents. Private: readable by the person who uploaded them and
    /// by nobody else.
    /// </summary>
    Document
}

/// <summary>A file that has been written to disk and can be linked to.</summary>
/// <param name="FileName">The generated name, which is also the last URL segment.</param>
/// <param name="Url">The path the app stores and renders.</param>
/// <param name="SizeBytes">Size as written, for logging and the upload response.</param>
/// <param name="ContentType">As supplied by the client; only ever an already-allowed type.</param>
public sealed record StoredMedia(string FileName, string Url, long SizeBytes, string ContentType);

/// <summary>
/// Writes uploads to the local disk under the web root and reads them back.
///
/// Deliberately not behind an interface. The whole point of the local-disk choice is that
/// it is a placeholder: the file handling lives in the API layer, where <c>IFormFile</c>
/// and the web root are, and the handlers only ever see the resulting URL. Moving to
/// object storage later means rewriting this class and nothing else — no handler signature
/// carries a file, and no DTO carries a path on this machine.
/// </summary>
public sealed class LocalMediaStore(IWebHostEnvironment environment, IOptions<StorageOptions> options)
{
    /// <summary>Sub-folder of the web root that every upload lands under.</summary>
    private const string RootFolder = "media";

    private readonly StorageOptions _options = options.Value;

    /// <summary>
    /// The directory uploads are written to, created on first use.
    ///
    /// Falls back to ContentRoot/wwwroot because some hosts (the integration test server
    /// among them) do not set a web root of their own even though the folder is right
    /// there. A missing folder is created rather than throwing, so a first upload on a
    /// fresh checkout works.
    /// </summary>
    public string RootPath
    {
        get
        {
            var root = string.IsNullOrWhiteSpace(environment.WebRootPath)
                ? Path.Combine(environment.ContentRootPath, "wwwroot")
                : environment.WebRootPath;

            var mediaRoot = Path.Combine(root, RootFolder);

            Directory.CreateDirectory(mediaRoot);

            return mediaRoot;
        }
    }

    /// <summary>
    /// Validates an upload and writes it out.
    ///
    /// The content type is checked before the bytes are read and the size is checked
    /// against the configured limit, so an oversized or unexpected file never reaches the
    /// disk. The stored name is generated rather than taken from the client, which is what
    /// makes path traversal impossible: there is no client-supplied text in the path at all.
    /// </summary>
    public async Task<StoredMedia> SaveAsync(
        IFormFile file,
        MediaKind kind,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new BusinessRuleException("The uploaded file is empty.", "EmptyUpload");
        }

        if (file.Length > _options.MaxDocumentBytes)
        {
            throw new BusinessRuleException(
                $"Files must be {_options.MaxDocumentBytes / (1024 * 1024)} MB or smaller.",
                "UploadTooLarge");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType) ||
            !_options.AllowedDocumentContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                $"Allowed file types: {string.Join(", ", _options.AllowedDocumentContentTypes)}.",
                "UnsupportedFileType");
        }

        // The extension comes from the validated content type, not from the client's
        // filename, so ".pdf" is only ever written for a real PDF and never for something
        // named "report.pdf" that is actually a script.
        var extension = ExtensionFor(file.ContentType);
        var fileName = $"{Guid.NewGuid():N}{extension}";

        var folder = KindFolder(kind);
        Directory.CreateDirectory(folder);

        await using (var stream = new FileStream(
                         Path.Combine(folder, fileName),
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         useAsync: true))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return new StoredMedia(fileName, UrlFor(kind, fileName), file.Length, file.ContentType);
    }

    /// <summary>
    /// Resolves a stored name to an absolute path inside the media root, or returns null.
    ///
    /// The name arrives from a URL, so it is untrusted: it is rejected outright if it
    /// contains any directory separator, a parent reference or a null character, and the
    /// resolved path is then confirmed to still be inside the root. Either check alone
    /// would do; both together mean a mistake in the first cannot become a read of
    /// arbitrary files.
    /// </summary>
    public string? Resolve(MediaKind kind, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (fileName.Contains('/') || fileName.Contains('\\') ||
            fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.Contains('\0'))
        {
            return null;
        }

        // Only the shapes SaveAsync actually produces.
        if (Path.GetExtension(fileName) is not { Length: > 1 } extension ||
            !_options.AllowedDocumentContentTypes
                .Select(ExtensionFor)
                .Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var folder = Path.GetFullPath(KindFolder(kind));
        var full = Path.GetFullPath(Path.Combine(folder, fileName));

        return full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : null;
    }

    /// <summary>Content type to serve a stored file with, inferred from its extension.</summary>
    public static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    private string KindFolder(MediaKind kind) => Path.Combine(RootPath, SlugFor(kind));

    private static string UrlFor(MediaKind kind, string fileName) =>
        $"/api/v1/media/{SlugFor(kind)}/{fileName}";

    /// <summary>
    /// The URL and folder name for a family. Written out rather than derived from the enum
    /// name so the segment stays kebab-case and matches the route: lowercasing
    /// <c>ProfileImage</c> would give "profileimage" and silently 404 against
    /// "profile-images".
    /// </summary>
    private static string SlugFor(MediaKind kind) => kind switch
    {
        MediaKind.ProfileImage => "profile-images",
        MediaKind.Document => "documents",
        _ => "media"
    };

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "application/pdf" => ".pdf",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
    };
}
