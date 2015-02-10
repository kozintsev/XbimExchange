﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Xbim.COBieLite.CollectionTypes;

// ReSharper disable once CheckNamespace
namespace Xbim.COBieLiteUK
{
    [JsonObject]
    public partial class AssetTypeCollectionType : ICollectionType<AssetTypeInfoType>, IEnumerable<AssetTypeInfoType>
    {
        public IEnumerator<AssetTypeInfoType> GetEnumerator()
        {
            return AssetType.OfType<AssetTypeInfoType>().GetEnumerator();
        }

        [XmlIgnore]
        [JsonIgnore]
        public List<AssetTypeInfoType> InnerList
        {
            get { return AssetType; }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return AssetType.OfType<AssetTypeInfoType>().GetEnumerator();
        }
    }
}
