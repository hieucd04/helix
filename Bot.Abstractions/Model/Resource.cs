using System;

namespace Helix.Bot.Abstractions
{
    public class Resource
    {
        Uri _uri;

        public int Id { get; }

        public bool IsExtractedFromHtmlDocument { get; }

        public bool IsInternal { get; set; }

        public Uri OriginalUri { get; }

        public string OriginalUrl { get; }

        // TODO:
        // public bool Localized { get; set; }

        public Uri ParentUri { get; }

        public ResourceType ResourceType { get; set; }

        public long? Size { get; set; }

        public StatusCode StatusCode { get; set; }

        public Uri Uri
        {
            get => _uri;
            set => _uri = StripFragment(value);
        }

        public Resource(int id, string originalUrl, Uri parentUri, bool isExtractedFromHtmlDocument)
        {
            Id = id;
            ParentUri = parentUri;
            OriginalUrl = originalUrl;
            IsExtractedFromHtmlDocument = isExtractedFromHtmlDocument;

            if (Uri.TryCreate(originalUrl, UriKind.RelativeOrAbsolute, out var relativeOrAbsoluteUri))
            {
                if (relativeOrAbsoluteUri.IsAbsoluteUri) OriginalUri = StripFragment(relativeOrAbsoluteUri);
                else if (Uri.TryCreate(parentUri, originalUrl, out var absoluteUri)) OriginalUri = StripFragment(absoluteUri);
                else StatusCode = StatusCode.MalformedUri;
            }
            else StatusCode = StatusCode.MalformedUri;

            if (StatusCode == default && UriSchemeIsNotSupported())
                StatusCode = StatusCode.UriSchemeNotSupported;

            _uri = OriginalUri;

            #region Local Functions

            bool UriSchemeIsNotSupported() { return OriginalUri.Scheme != "http" && OriginalUri.Scheme != "https"; }

            #endregion
        }

        static Uri StripFragment(Uri uri)
        {
            return string.IsNullOrWhiteSpace(uri.Fragment) ? uri : new UriBuilder(uri) { Fragment = string.Empty }.Uri;
        }
    }
}