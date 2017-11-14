﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Our.Umbraco.InnerContent.Models;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Web;

namespace Our.Umbraco.InnerContent.Helpers
{
    public static class InnerContentHelper
    {
        public static IEnumerable<IPublishedContent> ConvertInnerContentToPublishedContent(
            JArray items,
            IPublishedContent parentNode = null,
            int level = 0,
            bool preview = false)
        {
            return items.Select((x, i) => ConvertInnerContentToPublishedContent((JObject)x, parentNode, i, level, preview)).ToList();
        }

        public static IPublishedContent ConvertInnerContentToPublishedContent(JObject item,
            IPublishedContent parentNode = null,
            int sortOrder = 0,
            int level = 0,
            bool preview = false)
        {
            var publishedContentType = GetPublishedContentTypeFromItem(item);
            if (publishedContentType == null)
                return null;

            var propValues = item.ToObject<Dictionary<string, object>>();
            var properties = new List<IPublishedProperty>();

            foreach (var jProp in propValues)
            {
                var propType = publishedContentType.GetPropertyType(jProp.Key);
                if (propType != null)
                {
                    properties.Add(new DetachedPublishedProperty(propType, jProp.Value, preview));
                }
            }

            // Parse out the name manually
            object nameObj;
            if (propValues.TryGetValue("name", out nameObj))
            {
                // Do nothing, we just want to parse out the name if we can
            }

            // Parse out key manually
            object keyObj;
            if (propValues.TryGetValue("key", out keyObj))
            {
                // Do nothing, we just want to parse out the key if we can
            }

            // Get the current request node we are embedded in
            var pcr = UmbracoContext.Current.PublishedContentRequest;
            var containerNode = pcr != null && pcr.HasPublishedContent ? pcr.PublishedContent : null;

            var node = new DetachedPublishedContent(
                keyObj == null ? Guid.Empty : Guid.Parse(keyObj.ToString()),
                nameObj?.ToString(),
                publishedContentType,
                properties.ToArray(),
                containerNode,
                parentNode,
                sortOrder,
                level,
                preview);

            // Process children
            if (propValues.ContainsKey("children"))
            {
                var children = ConvertInnerContentToPublishedContent((JArray)propValues["children"], node, level + 1, preview);
                node.SetChildren(children);
            }

            return node;
        }

        internal static PreValueCollection GetPreValuesCollectionByDataTypeId(int dtdId)
        {
            var preValueCollection = (PreValueCollection)ApplicationContext.Current.ApplicationCache.RuntimeCache.GetCacheItem(
                string.Format(InnerContentConstants.PreValuesCacheKey, dtdId),
                () => ApplicationContext.Current.Services.DataTypeService.GetPreValuesCollectionByDataTypeId(dtdId));

            return preValueCollection;
        }

        internal static string GetContentTypeAliasFromItem(JObject item)
        {
            var contentTypeAliasProperty = item?[InnerContentConstants.ContentTypeAliasPropertyKey];
            return contentTypeAliasProperty?.ToObject<string>();
        }

        internal static Guid? GetContentTypeGuidFromItem(JObject item)
        {
            var contentTypeGuidProperty = item?[InnerContentConstants.ContentTypeGuidPropertyKey];
            return contentTypeGuidProperty?.ToObject<Guid?>();
        }

        internal static IContentType GetContentTypeFromItem(JObject item)
        {
            var contentTypeAlias = GetContentTypeAliasFromItem(item);
            return !contentTypeAlias.IsNullOrWhiteSpace()
                ? ApplicationContext.Current.Services.ContentTypeService.GetContentType(contentTypeAlias)
                : null;
        }

        internal static PublishedContentType GetPublishedContentTypeFromItem(JObject item)
        {
            var contentTypeAlias = string.Empty;

            // First we check if the item has a content-type GUID...
            var contentTypeGuid = GetContentTypeGuidFromItem(item);
            if (contentTypeGuid != null && contentTypeGuid.HasValue && contentTypeGuid.Value != Guid.Empty)
            {
                // If so, we attempt to get the content-type object
                var contentType = ApplicationContext.Current.Services.ContentTypeService.GetContentType(contentTypeGuid.Value);
                if (contentType != null)
                {
                    // Assign the content-type alias
                    contentTypeAlias = contentType.Alias;

                    // HACK: Force populating the cache [LK:2017-11-14]
                    // We need to return a `PublishedContentType` object, there's only one way to do this,
                    // e.g. via the `PublishedContentType.Get` method.
                    // Now, since we already have the `IContentType` instance, we can pop it in the static-cache,
                    // so that the `PublishedContentType.Get` method can immediately access it.
                    // See Umbraco source-code for the cache-item key:
                    // https://github.com/umbraco/Umbraco-CMS/blob/release-7.4.0/src/Umbraco.Core/Models/PublishedContent/PublishedContentType.cs#L135
                    var key = string.Format("PublishedContentType_{0}_{1}", PublishedItemType.Content.ToString().ToLowerInvariant(), contentTypeAlias.ToLowerInvariant());
                    ApplicationContext.Current.ApplicationCache.StaticCache.GetCacheItem(key, () => contentType);
                }
            }

            // If we don't have the content-type alias at this point, check if we can get it from the item
            if (string.IsNullOrEmpty(contentTypeAlias))
                contentTypeAlias = GetContentTypeAliasFromItem(item);

            if (string.IsNullOrEmpty(contentTypeAlias))
                return null;

            return PublishedContentType.Get(PublishedItemType.Content, contentTypeAlias);
        }
    }
}