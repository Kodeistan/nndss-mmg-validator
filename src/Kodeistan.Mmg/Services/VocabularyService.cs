﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kodeistan.Mmg.Services
{
    public sealed class InMemoryVocabularyService : IVocabularyService
    {
        private static List<Tuple<string, string, string>> YNU = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "Y", "Yes", "CDC" ),
            new Tuple<string, string, string>( "N", "No", "CDC" ),
            new Tuple<string, string, string>( "U", "Unknown", "NULLFL" ),
        };

        private static List<Tuple<string, string, string>> MFU = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "M", "Male", "CDC" ),
            new Tuple<string, string, string>( "F", "Female", "CDC" ),
            new Tuple<string, string, string>( "U", "Unknown", "NULLFL" ),
        };

        private static List<Tuple<string, string, string>> Ethnicity = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "2135-2", "Hispanic or Latino", "CDC" ),
            new Tuple<string, string, string>( "2186-5", "Not Hispanic or Latino", "CDC" ),
            new Tuple<string, string, string>( "OTH", "other", "NULLFL" ),
            new Tuple<string, string, string>( "UNK", "unknown", "NULLFL" ),
        };

        private static List<Tuple<string, string, string>> Race = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "1002-5", "American Indian or Alaska Native", "CDC" ),
            new Tuple<string, string, string>( "2028-9", "Asian", "CDC" ),
            new Tuple<string, string, string>( "ASKU", "asked but unknown", "NULLFL" ),
            new Tuple<string, string, string>( "2054-5", "Black or African American", "CDC" ),
            new Tuple<string, string, string>( "2076-8", "Native Hawaiian or Other Pacific Islander", "CDC" ),
            new Tuple<string, string, string>( "NI", "NoInformation", "NULLFL" ),
            new Tuple<string, string, string>( "NASK", "not asked", "NULLFL" ),
            new Tuple<string, string, string>( "2131-1", "Other Race", "CDC" ),
            new Tuple<string, string, string>( "PHC1175", "Refused to answer", "PHVS" ),
            new Tuple<string, string, string>( "UNK", "unknown", "CDC" ),
            new Tuple<string, string, string>( "2106-3", "White", "CDC" ),
        };

        private static List<Tuple<string, string, string>> DurationUnit = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "d", "day [time]", "UCUM" ),
            new Tuple<string, string, string>( "h", "hour [time]", "UCUM" ),
            new Tuple<string, string, string>( "min", "minute [time]", "UCUM" ),
            new Tuple<string, string, string>( "mo", "month [time]", "UCUM" ),
            new Tuple<string, string, string>( "s", "second [time]", "UCUM" ),
            new Tuple<string, string, string>( "UNK", "unknown", "NULLFL" ),
            new Tuple<string, string, string>( "wk", "week [time]", "UCUM" ),
            new Tuple<string, string, string>( "a", "year [time]", "UCUM" ),
        };

        private static List<Tuple<string, string, string>> AgeUnit = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "d", "day [time]", "UCUM" ),
            new Tuple<string, string, string>( "h", "hour [time]", "UCUM" ),
            new Tuple<string, string, string>( "min", "minute [time]", "UCUM" ),
            new Tuple<string, string, string>( "mo", "month [time]", "UCUM" ),
            new Tuple<string, string, string>( "OTH", "other", "NULLFL" ),
            new Tuple<string, string, string>( "s", "second [time]", "UCUM" ),
            new Tuple<string, string, string>( "UNK", "unknown", "NULLFL" ),
            new Tuple<string, string, string>( "wk", "week [time]", "UCUM" ),
            new Tuple<string, string, string>( "a", "year [time]", "UCUM" ),
        };

        private static List<Tuple<string, string, string>> DiseaseAcquiredJurisdiction = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "PHC244", "Indigenous", "CDC" ),
            new Tuple<string, string, string>( "C1512888", "International", "CDC" ),
            new Tuple<string, string, string>( "PHC245", "In State,Out of jurisdiction", "CDC" ),
            new Tuple<string, string, string>( "PHC246", "Out of state", "CDC" ),
            new Tuple<string, string, string>( "UNK", "Unknown", "NULLFL" ),
            new Tuple<string, string, string>( "PHC1274", "Yes imported, but not able to determine source state and/or country", "CDC" ),
        };

        private static List<Tuple<string, string, string>> CaseClassStatus = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "410605003", "Confirmed present", "CDC" ),
            new Tuple<string, string, string>( "PHC178", "Not a Case", "CDC" ),
            new Tuple<string, string, string>( "2931005", "Probable diagnosis", "CDC" ),
            new Tuple<string, string, string>( "415684004", "Suspected", "CDC" ),
            new Tuple<string, string, string>( "UNK", "Unknown", "NULLFL" ),
        };

        private static List<Tuple<string, string, string>> CaseTransmissionMode = new List<Tuple<string, string, string>>() {
            new Tuple<string, string, string>( "416380006", "Airborne transmission", "CDC" ),
            new Tuple<string, string, string>( "409700000", "Animal to human transmission", "CDC" ),
            new Tuple<string, string, string>( "420014008", "Blood borne transmission", "CDC" ),
            new Tuple<string, string, string>( "461551000124100", "Droplet transmission", "CDC" ),
            new Tuple<string, string, string>( "416086007", "Food-borne transmission", "CDC" ),
            new Tuple<string, string, string>( "418375005", "Indeterminate disease transmission mode", "CDC" ),
            new Tuple<string, string, string>( "PHC142", "Mechanical", "CDC" ),
            new Tuple<string, string, string>( "416085006", "Nosocomial transmission", "CDC" ),
            new Tuple<string, string, string>( "OTH", "Other", "NULLFL" ),
            new Tuple<string, string, string>( "417564009", "Sexual transmission", "CDC" ),
            new Tuple<string, string, string>( "420193003", "Transdermal transmission", "CDC" ),
            new Tuple<string, string, string>( "417409004", "Transplacental transmission", "CDC" ),
            new Tuple<string, string, string>( "418427004", "Vector-borne transmission", "CDC" ),
            new Tuple<string, string, string>( "418117003", "Water-borne transmission", "CDC" )
        };

        public VocabularyValidationResult IsValid(string conceptCode, string conceptName, string conceptCodeSystem, string valueSetCode)
        {
            List<Tuple<string, string, string>> valueSet = new List<Tuple<string, string, string>>();
            
            if (valueSetCode.Equals("PHVS_YesNoUnknown_CDC"))
            {
                valueSet = YNU;
            }
            else if (valueSetCode.Equals("PHVS_Sex_MFU"))
            {
                valueSet = MFU;
            }
            else if (valueSetCode.Equals("PHVS_RaceCategory_CDC_NullFlavor"))
            {
                valueSet = Race;
            }
            else if (valueSetCode.Equals("PHVS_EthnicityGroup_CDC_Unk"))
            {
                valueSet = Ethnicity;
            }
            else if (valueSetCode.Equals("PHVS_DurationUnit_CDC"))
            {
                valueSet = DurationUnit;
            }
            else if (valueSetCode.Equals("PHVS_AgeUnit_UCUM"))
            {
                valueSet = AgeUnit;
            }
            else if (valueSetCode.Equals("PHVS_DiseaseAcquiredJurisdiction_NND"))
            {
                valueSet = DiseaseAcquiredJurisdiction;
            }
            else if (valueSetCode.Equals("PHVS_CaseClassStatus_NND"))
            {
                valueSet = CaseClassStatus;
            }
            else if (valueSetCode.Equals("PHVS_CaseTransmissionMode_NND"))
            {
                valueSet = CaseTransmissionMode;
            }
            else
            {
                // if it's not in our in-memory list, just skip it for now and assume it's valid
                return new VocabularyValidationResult(
                    isCodeValid: true,
                    isNameValid: true,
                    isSystemValid: true);
            }

            bool isCodeValid = false;
            bool isDescriptionValid = false;

            foreach (var t in valueSet)
            {
                if (t.Item1 == conceptCode)
                {
                    isCodeValid = true;
                    if (t.Item2.Equals(conceptName, StringComparison.OrdinalIgnoreCase))
                    {
                        isDescriptionValid = true;
                    }
                    break;
                }
            }

            var result = new VocabularyValidationResult(
                isCodeValid: isCodeValid,
                isNameValid: isDescriptionValid,
                isSystemValid: true); // assume true for code system lookups until we can build an API

            result.ConceptCode = conceptCode;
            result.ConceptName = conceptName;
            result.ConceptCodeSystem = conceptCodeSystem;

            return result;
        }
    }
}
