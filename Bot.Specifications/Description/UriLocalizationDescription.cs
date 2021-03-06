using System;
using System.Collections.Generic;
using Helix.Bot.Abstractions;
using Helix.Specifications;
using Newtonsoft.Json;

namespace Helix.Bot.Specifications
{
    internal class UriLocalizationDescription : TheoryDescription<Configurations, Uri, Uri, Type>
    {
        public UriLocalizationDescription()
        {
            ReplaceHostNameMatchingConfiguredRemoteHostWithHostNameOfStartUri();

            DoesNothingToUriWhoseHostNameIsDifferentFromTheConfiguredRemoteHost();

            ThrowExceptionIfArgumentNull();
        }

        void DoesNothingToUriWhoseHostNameIsDifferentFromTheConfiguredRemoteHost()
        {
            var configurations = new Configurations(JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                { nameof(Configurations.StartUri), "http://www.sanity.com" },
                { nameof(Configurations.RemoteHost), "www.helix.com" }
            }));
            AddTheoryDescription(configurations, new Uri("http://www.sanity.com/anything"), new Uri("http://www.sanity.com/anything"));
        }

        void ReplaceHostNameMatchingConfiguredRemoteHostWithHostNameOfStartUri()
        {
            var configurations = new Configurations(JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                { nameof(Configurations.StartUri), "http://192.168.1.2" },
                { nameof(Configurations.RemoteHost), "www.helix.com" }
            }));
            AddTheoryDescription(configurations, new Uri("http://www.helix.com/anything"), new Uri("http://192.168.1.2/anything"));
        }

        void ThrowExceptionIfArgumentNull() { AddTheoryDescription(p2: null, p4: typeof(ArgumentNullException)); }
    }
}