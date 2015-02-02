﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using Xbim.COBieLite;
using Xbim.Ifc2x3.Kernel;
using Xbim.Ifc2x3.MeasureResource;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.QuantityResource;
using Xbim.IO;
using XbimExchanger.IfcHelpers;

namespace XbimExchanger.COBieLiteToIfc
{
    public class CoBieLiteToIfcExchanger : XbimExchanger<FacilityType, XbimModel>
    {
        #region Nested Structures
        public struct NamedProperty
        {
            public string PropertySetName;
            public string PropertyName;

            public NamedProperty(string propertySetName, string propertyName )
                : this()
            {
                PropertyName = propertyName;
                PropertySetName = propertySetName;
            }
        }
        #endregion

        #region Static members and functions
        static readonly IDictionary<string, NamedProperty> CobieToIfcPropertyMap = new Dictionary<string, NamedProperty>();


        static CoBieLiteToIfcExchanger()
        {

            var configMap = new ExeConfigurationFileMap { ExeConfigFilename = "COBieAttributes.config" };
            var config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            var cobiePropertyMaps = (AppSettingsSection)config.GetSection("COBiePropertyMaps");

            foreach (KeyValueConfigurationElement keyVal in cobiePropertyMaps.Settings)
            {
                var values = keyVal.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var selected = values.FirstOrDefault();
                if (selected != null)
                {
                    var names = selected.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                    if (names.Length == 2)
                        CobieToIfcPropertyMap.Add(keyVal.Key, new NamedProperty(names[0], names[1]));
                }
            }

        }
        #endregion

        #region Fields

        private readonly Dictionary<IfcObject, List<IfcPropertySetDefinition>> _objectsToPropertySets = new Dictionary<IfcObject, List<IfcPropertySetDefinition>>();



        #endregion

        #region Properties

        public IfcUnitConverter? DefaultLinearUnit;
        public IfcUnitConverter? DefaultAreaUnit;
        public IfcUnitConverter? DefaultVolumeUnit;
        public CurrencyUnitSimpleType? DefaultCurrencyUnit;

        #endregion

        #region Constructors
        public CoBieLiteToIfcExchanger(FacilityType facility, XbimModel repository)
            : base(facility, repository)
        {
            LoadPropertySetDefinitions();
        }
        #endregion

        #region Converters
        public IfcBuilding Convert(FacilityType facility)
        {
            var mapping = GetOrCreateMappings<MappingFacilityTypeToIfcBuilding>();
            var building = mapping.GetOrCreateTargetObject(facility.externalID);
            return mapping.AddMapping(facility, building);

        }

        public override XbimModel Convert()
        {
            Convert(SourceRepository);
            return TargetRepository;
        }
        #endregion

        #region Methods


        private void LoadPropertySetDefinitions()
        {
            var relProps = TargetRepository.Instances.OfType<IfcRelDefinesByProperties>().ToList();
            foreach (var relProp in relProps)
            {
                foreach (var ifcObject in relProp.RelatedObjects)
                {
                    List<IfcPropertySetDefinition> propDefinitionList;
                    if (!_objectsToPropertySets.TryGetValue(ifcObject, out propDefinitionList)) //if it hasn't got any, add an empty list
                    {
                        propDefinitionList = new List<IfcPropertySetDefinition>();
                        _objectsToPropertySets.Add(ifcObject, propDefinitionList);
                    }
                    propDefinitionList.Add(relProp.RelatingPropertyDefinition);
                }
            }
        }
        #endregion


        /// <summary>
        /// Creates the property and if required the property set, populates them with the correct values and adds them to the IfcObject
        /// </summary>
        /// <param name="ifcObject">Object to associate the property with</param>
        /// <param name="valueBaseType">COBie value to populate the property with</param>
        /// <param name="cobiePropertyName">Name of the COBie property being mapped</param>
        /// <param name="defaultUnits">Units to use if the COBie property does not specify</param>
        internal void CreatePropertySingleValue(IfcObject ifcObject, ValueBaseType valueBaseType, string cobiePropertyName, IfcUnitConverter? defaultUnits)
        {
            if (valueBaseType == null) return; //nothing to do
            NamedProperty namedProperty;
            if (CobieToIfcPropertyMap.TryGetValue(cobiePropertyName, out namedProperty))
            {

                var actualUnits = new IfcUnitConverter(valueBaseType.UnitName);
                if (actualUnits.IsUndefined && defaultUnits.HasValue) actualUnits = defaultUnits.Value;
                List<IfcPropertySetDefinition> propertySetDefinitionList;
                if (!_objectsToPropertySets.TryGetValue(ifcObject, out propertySetDefinitionList)) //see what sets we have against this object
                {
                    propertySetDefinitionList = new List<IfcPropertySetDefinition>();
                    _objectsToPropertySets.Add(ifcObject, propertySetDefinitionList);
                    //simplistic way to decide if this should be a quantity, IFC 4 specifies the name starts with QTO, under 2x3 most vendors have gone for BaseQuantities
                    if (namedProperty.PropertySetName.StartsWith("qto_", true, CultureInfo.InvariantCulture) ||
                        namedProperty.PropertySetName.StartsWith("basequantities", true, CultureInfo.InvariantCulture))
                    {
                        try
                        {
                            var cobieValue = ConvertValueBaseType<double>(valueBaseType);
                            if (actualUnits.IsUndefined)
                                throw new ArgumentException("Invalid unit type " + actualUnits.UserDefinedSiUnitName + " has been pass to CreatePropertySingleValue");
                            var propertySet = TargetRepository.Instances.New<IfcElementQuantity>();
                            propertySet.Name = namedProperty.PropertySetName;
                            IfcPhysicalQuantity quantity;

                            switch (actualUnits.UnitName) //they are all here for future proofing, time, mass and count though are not really used by COBie
                            {
                                case IfcUnitEnum.AREAUNIT:
                                    quantity = TargetRepository.Instances.New<IfcQuantityArea>(q => q.AreaValue = new IfcAreaMeasure(cobieValue));
                                    break;
                                case IfcUnitEnum.LENGTHUNIT:
                                    quantity = TargetRepository.Instances.New<IfcQuantityLength>(q => q.LengthValue = new IfcLengthMeasure(cobieValue));
                                    break;
                                case IfcUnitEnum.MASSUNIT:
                                    quantity = TargetRepository.Instances.New<IfcQuantityWeight>(q => q.WeightValue = new IfcMassMeasure(cobieValue));
                                    break;
                                case IfcUnitEnum.TIMEUNIT:
                                    quantity = TargetRepository.Instances.New<IfcQuantityTime>(q => q.TimeValue = new IfcTimeMeasure(cobieValue));
                                    break;
                                case IfcUnitEnum.VOLUMEUNIT:
                                    quantity = TargetRepository.Instances.New<IfcQuantityVolume>(q => q.VolumeValue = new IfcVolumeMeasure(cobieValue));
                                    break;
                                case IfcUnitEnum.USERDEFINED: //we will treat this as Item for now
                                    quantity = TargetRepository.Instances.New<IfcQuantityCount>(q => q.CountValue = new IfcCountMeasure(cobieValue));
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                            quantity.Description = "Converted from COBie " + cobiePropertyName;
                            quantity.Name = namedProperty.PropertyName;
                            propertySet.Quantities.Add(quantity);
                            var relDef = TargetRepository.Instances.New<IfcRelDefinesByProperties>();
                            relDef.RelatingPropertyDefinition = propertySet;
                            relDef.RelatedObjects.Add(ifcObject);
                           
                            return;
                        }
                        catch (Exception e)
                        {
                            throw new Exception("Failed to convert a COBie Value to and Ifc Quantity. " + e.Message);
                        }
                    }

                }
                else //need to use an existing PropertySet definition
                {

                }

            }
            throw new ArgumentException("Incorrect property map", "cobiePropertyName");
        }

        private TType ConvertValueBaseType<TType>(ValueBaseType valueBaseType)
        {
            var decimalType = valueBaseType as DecimalValueType;
            if (decimalType != null && decimalType.DecimalValueSpecified)
                return (TType)System.Convert.ChangeType(decimalType.DecimalValue, typeof(TType));
            var stringType = valueBaseType as StringValueType;
            if (stringType != null)
                return (TType)System.Convert.ChangeType(stringType.StringValue, typeof(TType));
            var integerType = valueBaseType as IntegerValueType;
            if (integerType != null)
                return (TType)System.Convert.ChangeType(integerType.IntegerValue, typeof(TType));
            var booleanType = valueBaseType as BooleanValueType;
            if (booleanType != null && booleanType.BooleanValueSpecified)
                return (TType)System.Convert.ChangeType(booleanType.BooleanValue, typeof(TType));
            return default(TType);
        }
    }
}
