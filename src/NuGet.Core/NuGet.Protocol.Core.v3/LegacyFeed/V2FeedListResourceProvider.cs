﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.LocalRepositories;

namespace NuGet.Protocol.LegacyFeed
{
    public class V2FeedListResourceProvider : ResourceProvider
    {

        public V2FeedListResourceProvider() : base(
            typeof(ListResource),
            nameof(V2FeedListResourceProvider),
            nameof(LocalPackageListResourceProvider))
        {
        }
        public override async Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source,
            CancellationToken token)
        {
            ListResource resource = null;

            if (await source.GetFeedType(token) == FeedType.HttpV2)
            {
                var httpSource = await source.GetResourceAsync<HttpSourceResource>(token);

                var serviceDocument = await source.GetResourceAsync<ODataServiceDocumentResourceV2>(token);
                var parser = new V2FeedParser(httpSource.HttpSource, serviceDocument.BaseAddress, source.PackageSource);
                var feedCapabilityResource = new LegacyFeedCapabilityResourceV2Feed(parser, serviceDocument.BaseAddress);
                resource = new V2FeedListResource(parser,feedCapabilityResource);
            }
            return new Tuple<bool, INuGetResource>(resource != null, resource);
        }
    }

//    var url = source.PackageSource.Source;
//            if (source.PackageSource.ProtocolVersion == 2 ||
//                (source.PackageSource.IsHttp &&
//                 !url.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
//            {
//                    var serviceDocument = await source.GetResourceAsync<ODataServiceDocumentResourceV2>(token);
//                    if (serviceDocument != null) { 
//                        listCommandResource = new ListCommandResource(serviceDocument.BaseAddress);
//}
//                }

//            }
}