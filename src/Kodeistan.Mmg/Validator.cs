using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using HL7.Dotnetcore;
using Kodeistan.Mmg.Model;
using Kodeistan.Mmg.Model.HL7V251;
using Kodeistan.Mmg.Services;

namespace Kodeistan.Mmg
{
    public class Validator : IValidator
    {
        private readonly IVocabularyService _vocabService;
        private readonly IMmgService _mmgService;

        public Validator()
        {
            _vocabService = new FakeVocabularyService();
            _mmgService = new FileSystemMmgService();
        }

        public Validator(IVocabularyService vocabService, IMmgService mmgService)
        {
            _vocabService = vocabService;
            _mmgService = mmgService;
        }

        public ValidationResult ValidateMessage(string hl7v2message)
        {
            ValidationResult result = new ValidationResult();
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            #region Parse HL7v2 message into C# object model
            Message message = null;

            try
            {
                message = new Message(hl7v2message);
                message.ParseMessage();
            }
            catch (Exception ex)
            {
                validationMessages.Add(new ValidationMessage(ex));
            }

            if (validationMessages.Count == 1 || message == null)
            {
                // The message failed to parse due to an exception in the parsing logic, 
                // so we should stop here and just return the error. There is no point in 
                // further processing.
                result.ValidationMessages = validationMessages;
                result.Elapsed = sw.Elapsed;

                return result;
            }

            var segments = message.Segments();
            #endregion

            #region Fill in profile identifier and local record ID for the validation result C# object
            var mshSegment = message.Segments("MSH").FirstOrDefault();
            if (mshSegment == null)
            {
                validationMessages.Add(
                    new ValidationMessage(
                        Severity.Error,
                        ValidationMessageType.Structural,
                        $"MSH segment was not found",
                        $""));

                result.ValidationMessages = validationMessages;
                result.Elapsed = sw.Elapsed;

                return result;
            }
            else if (mshSegment.GetAllFields().Count < 21 || string.IsNullOrEmpty(mshSegment.Fields(21).Value))
            {
                validationMessages.Add(
                    new ValidationMessage(
                        Severity.Error,
                        ValidationMessageType.Structural,
                        $"MSH segment contains no profile identifier",
                        $"MSH[1].21"));

                result.ValidationMessages = validationMessages;
                result.Elapsed = sw.Elapsed;

                return result;
            }

            // we need to keep track of these values after validation for debugging and other purposes, so let's just go ahead and fill the data values in right now
            result.Profile = mshSegment.Fields(21).Repetitions().Last().Components(1).SubComponents(1).Value;

            var obrSegment = message.Segments("OBR").FirstOrDefault();
            if (obrSegment == null)
            {
                validationMessages.Add(
                    new ValidationMessage(
                        Severity.Error,
                        ValidationMessageType.Structural,
                        $"OBR segment was not found",
                        $""));

                result.ValidationMessages = validationMessages;
                result.Elapsed = sw.Elapsed;

                return result;
            }
            else
            {
                result.LocalRecordId = obrSegment.Fields(3).Components(1).Value;
            }
            #endregion

            #region Get the condition code and national reporting jurisdiction
            result.Condition = obrSegment.Fields(31).Components(2).Value;
            result.ConditionCode = obrSegment.Fields(31).Components(1).Value;
            result.NationalReportingJurisdiction = string.Empty;
            var reportingJurisdictionSegment = message.Segments("OBX").Where(s => s.Fields(3).Value.StartsWith("77968-6")).FirstOrDefault();
            if (reportingJurisdictionSegment != null)
            {
                result.NationalReportingJurisdiction = reportingJurisdictionSegment.Fields(5).Components(2).Value;
            }
            #endregion

            #region Get MMG C# model object
            // Get the correct MMG for this message based on the profile identifier
            string dateTimeOfMessageString = mshSegment.Fields(7).Value.Substring(0, 14);
            if (DateTime.TryParseExact(dateTimeOfMessageString,
                       "yyyyMMddhhmmss",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out DateTime dt))
            {
                result.MessageCreated = dt;
            }

            MessageMappingGuide messageMappingGuide = _mmgService.Get(mshSegment.Fields(21).Value, result.ConditionCode);
            #endregion

            #region Validate HL7v2 message content
            // check content
            foreach (var segment in segments)
            {
                switch (segment.Name)
                {
                    case "OBX":
                        var obxValidationMessages = ValidateOBX(segment, messageMappingGuide);
                        validationMessages.AddRange(obxValidationMessages);
                        break;
                    case "PID":
                        break;
                }
            }
            #endregion

            #region Validate required fields
            // make sure that all required data elements are present in the HL7 message
            var requiredValidationMessages = ValidateRequiredFields(message, messageMappingGuide);
            validationMessages.AddRange(requiredValidationMessages);
            #endregion

            #region Validate no extraneous fields are present
            // make sure that there are no data included in the HL7 message's OBX segments that are not defined in the message mapping guide
            var extraneousDataMessages = ValidateExtraneousOBXSegments(message, messageMappingGuide);
            validationMessages.AddRange(extraneousDataMessages);
            #endregion

            sw.Stop();

            result.ValidationMessages = validationMessages;
            result.Elapsed = sw.Elapsed;
            result.Created = DateTimeOffset.Now;

            return result;
        }

        /// <summary>
        /// Ensure a required data element exists in the message. For OBX-5-based data elements, this means ensuring the OBX segment exists and 
        /// that it has a non-empty value. For OBX values other than OBX-5, there is a special relationship to examine.
        /// </summary>
        /// <param name="message">HL7v2 message</param>
        /// <param name="messageMappingGuide">The message mapping guide</param>
        /// <returns>List of validation messages</returns>
        private List<ValidationMessage> ValidateRequiredFields(Message message, MessageMappingGuide messageMappingGuide)
        {
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();

            // check required fields are present
            foreach (DataElement element in messageMappingGuide.Elements
                .Where(de => de.Priority == Priority.Required))
            {
                var mapping = element.Mappings.Hl7v251;
                string segmentType = mapping.SegmentType.ToString();

                bool found = segmentType == "OBX" ? false : true;
                foreach (Segment segment in message.Segments()
                    .Where(s => s.Name == segmentType))
                {
                    if (segmentType == "OBX" && (mapping.FieldPosition == 5 || mapping.FieldPosition.HasValue == false) && segment.Fields(3) != null && segment.Fields(3).Components(1).Value.Equals(mapping.Identifier))
                    {
                        found = true;
                        if (segment.Fields(5) == null || string.IsNullOrWhiteSpace(segment.Fields(5).Value))
                        {
                            validationMessages.Add(new ValidationMessage(
                                Severity.Error,
                                ValidationMessageType.Content, 
                                $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is required, but was not found in the message", 
                                $"OBX[{segment.Fields(1).Value}]"));
                        }
                        break;
                    }
                    if (segmentType == "OBX" && mapping.FieldPosition.HasValue && mapping.FieldPosition.Value != 5)
                    {
                        found = true;

                        int pos = mapping.FieldPosition.Value;

                        var relatedElement = messageMappingGuide.Elements.FirstOrDefault(e => e.Id == element.RelatedElementId);
                        var relatedMapping = relatedElement.Mappings.Hl7v251;
                        string relatedSegmentType = relatedMapping.SegmentType.ToString();

                        var relatedSegment = message.Segments().Where(s => s.Fields(3).Equals(relatedMapping.Identifier)).FirstOrDefault();

                        if (relatedSegment.Fields(pos) == null || string.IsNullOrWhiteSpace(segment.Fields(pos).Value))
                        {
                            validationMessages.Add(new ValidationMessage(
                                Severity.Error,
                                ValidationMessageType.Content, 
                                $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is required, but was not found in the message",
                                $"OBX[{relatedSegment.Fields(1).Value}].{pos}"));
                        }

                        break;
                    }
                }

                if (!found)
                {
                    validationMessages.Add(new ValidationMessage(
                        Severity.Error,
                        ValidationMessageType.Content,
                        $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is required, but was not found in the message", 
                        $""));
                }
            }

            return validationMessages;
        }

        private List<ValidationMessage> ValidateExtraneousOBXSegments(Message message, MessageMappingGuide messageMappingGuide)
        {
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();

            foreach (Segment segment in message.Segments()
                    .Where(s => s.Name == "OBX"))
            {
                string identifier = segment.Fields(3).Components(1).Value;

                DataElement matchingElement = messageMappingGuide.Elements
                    .Where(de => de.Mappings.Hl7v251.SegmentType == Model.HL7V251.SegmentType.OBX)
                    .Where(de => de.Mappings.Hl7v251.Identifier == identifier)
                    .FirstOrDefault();

                if (matchingElement == null)
                {
                    validationMessages.Add(new ValidationMessage(
                        Severity.Warning,
                        ValidationMessageType.Structural,
                        $"Data was found in an OBX segment with identifier '{identifier}', but no data elements corresponding with this identifier were found in the '{messageMappingGuide.Name}' message mapping guide.",
                        $"OBX[{segment.Fields(1).Value}].3"));
                }
            }

            return validationMessages;
        }

        public static Dictionary<string, string> DATA_TYPE_LOOKUP = new Dictionary<string, string>()
        {
            { "OBX-6", "CE" },
            { "OBX-7", "ST" },
            { "OBX-8", "IS" },
            { "OBX-9", "NM" },
            { "OBX-10", "ID" },
            { "OBX-11", "ID" },
            { "OBX-12", "TS" },
            { "OBX-13", "ST" },
            { "OBX-14", "TS" },
            { "OBX-15", "CE" },
            { "OBX-16", "XCN" },
            { "OBX-17", "CE" },
            { "OBX-18", "EI" },
            { "OBX-19", "TS" },
            { "OBX-23", "XON" },
            { "OBX-24", "XAD" },
            { "OBX-25", "XCN" },

            { "PID-8", "CWE" },
            { "PID-10", "CWE" },
            { "PID-15", "CWE" },
            { "PID-16", "CWE" },
            { "PID-17", "CWE" },
            { "PID-22", "CWE" },
            { "PID-26", "CWE" },
            { "PID-27", "CWE" },
            { "PID-28", "CWE" },
            { "PID-32", "CWE" },
            { "PID-35", "CWE" },
            { "PID-36", "CWE" },
            { "PID-38", "CWE" },
            { "PID-39", "CWE" },
        };

        private List<ValidationMessage> ValidateCE(HL7.Dotnetcore.Field field, DataElement element)
        {
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();



            return validationMessages;
        }

        private List<ValidationMessage> ValidateOBX(Segment segment, MessageMappingGuide messageMappingGuide)
        {
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();

            if (!segment.Name.Equals("OBX"))
            {
                // don't bother validating OBX segments if the segment isn't an OBX
                return validationMessages;
            }

            int instance = 1;

            foreach (DataElement element in messageMappingGuide.Elements)
            {
                var mapping = element.Mappings.Hl7v251;

                if (mapping.SegmentType != SegmentType.OBX)
                {
                    continue;
                }

                string segmentType = mapping.SegmentType.ToString();
                string segmentIdentifier = segment.Fields(3).Components(1).Value;

                if (mapping.Identifier == segmentIdentifier)
                {
                    // do a check for those rare instances of non-OBX-5 fields, and do the related lookup on them to get the related data element and not the root OBX data element
                    List<DataElement> relatedElements = messageMappingGuide.Elements
                        .Where(de => de.RelatedElementId == element.Id)
                        .Where(de => de.Mappings.Hl7v251.FieldPosition.HasValue)
                        .ToList();

                    foreach (var relatedElement in relatedElements)
                    {
                        string lookup = relatedElement.Mappings.Hl7v251.SegmentType.ToString() + "-" + relatedElement.Mappings.Hl7v251.FieldPosition.Value.ToString();
                        if (DATA_TYPE_LOOKUP.ContainsKey(lookup))
                        {
                            string datatype = DATA_TYPE_LOOKUP[lookup];
                            var field = segment.Fields(relatedElement.Mappings.Hl7v251.FieldPosition.Value);

                            if (datatype == "CE")
                            {
                                string conceptCode = field.Value;

                                if (field.IsComponentized)
                                {
                                    conceptCode = field.Components(1).Value;
                                }

                                bool found = _vocabService.IsConceptCodeValid(conceptCode, "", "", relatedElement.ValueSetCode);

                                if (!found)
                                {
                                    ValidationMessage illegalConceptCodeMessage = new ValidationMessage(
                                        severity: Severity.Warning,
                                        messageType: ValidationMessageType.Structural,
                                        content: $"Data element '{relatedElement.Name}' in with identifier '{relatedElement.Mappings.Hl7v251.Identifier}' is a coded element associated with the value set '{relatedElement.ValueSetCode}'. However, the concept code '{conceptCode}' in the message was not found as a valid concept for this value set",
                                        path: $"OBX[{segment.Fields(1).Value}].{relatedElement.Mappings.Hl7v251.FieldPosition.Value}",
                                        pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                    validationMessages.Add(illegalConceptCodeMessage);
                                }
                            }
                        }
                    }

                    var dataTypeValidationMessages = ValidateOBXDataType(segment, element);
                    validationMessages.AddRange(dataTypeValidationMessages);

                    if (segment.Fields(5).HasRepetitions && element.Repetitions <= 1)
                    {
                        // element is defined as not repeating, but has repeats in a message
                        ValidationMessage illegalRepeatsMessage = new ValidationMessage(
                            severity: Severity.Error,
                            messageType: ValidationMessageType.Structural,
                            content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is not a repeating element, but has repeating data in the message",
                            path: $"OBX[{segment.Fields(1).Value}].5",
                            pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                        validationMessages.Add(illegalRepeatsMessage);
                    }

                    if ( (segment.Fields(2).Value.Equals("CWE") && mapping.DataType == Model.HL7V251.DataType.CWE) || (segment.Fields(2).Value.Equals("CE") && mapping.DataType == Model.HL7V251.DataType.CE) )
                    {
                        var field = segment.Fields(5);
                        string valueSetCode = element.ValueSetCode;

                        if (field.HasRepetitions)
                        {
                            for (int i = 0; i < field.Repetitions().Count; i++)
                            {
                                var repetition = field.Repetitions()[i];
                                
                                string conceptCode = repetition.Components(1).Value;
                                string conceptName = repetition.Components(2).Value;
                                string conceptCodeSystem = repetition.Components(3).Value;
                                bool found = _vocabService.IsConceptCodeValid(conceptCode, conceptName, conceptCodeSystem, valueSetCode);

                                if (!found)
                                {
                                    ValidationMessage illegalConceptCodeMessage = new ValidationMessage(
                                        severity: Severity.Warning,
                                        messageType: ValidationMessageType.Vocabulary,
                                        content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is a coded element associated with the value set '{valueSetCode}'. However, the concept code '{conceptCode}' in the message was not found as a valid concept for this value set in repetition {i + 1}",
                                        path: $"OBX[{segment.Fields(1).Value}].5[{i + 1}]",
                                        pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                    validationMessages.Add(illegalConceptCodeMessage);
                                }
                            }
                        }
                        else
                        {
                            // it's a coded element, so check for vocabulary
                            string conceptCode = segment.Fields(5).Components(1).Value;
                            string conceptName = segment.Fields(5).Components(2).Value;
                            string conceptCodeSystem = segment.Fields(5).Components(3).Value;
                            bool found = _vocabService.IsConceptCodeValid(conceptCode, conceptName, conceptCodeSystem, valueSetCode);

                            if (!found)
                            {
                                ValidationMessage illegalConceptCodeMessage = new ValidationMessage(
                                    severity: Severity.Warning,
                                    messageType: ValidationMessageType.Vocabulary,
                                    content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is a coded element associated with the value set '{valueSetCode}'. However, the concept code '{conceptCode}' in the message was not found as a valid concept for this value set",
                                    path: $"OBX[{segment.Fields(1).Value}].5",
                                    pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                validationMessages.Add(illegalConceptCodeMessage);
                            }
                        }
                    }

                    instance++; // TODO: Add instance number to the path string, e.g. if this OBX shows up multiple times (as it might with repeating groups)
                }
            }

            return validationMessages;
        }

        /// <summary>
        /// Ensure that the data type specified in the message mapping guide is what appears in the HL7 message
        /// </summary>
        /// <param name="segment">The HL7 segment to process</param>
        /// <param name="element">The data element to validate</param>
        /// <returns>List of validation messages</returns>
        private List<ValidationMessage> ValidateOBXDataType(Segment segment, DataElement element)
        {
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();

            var mapping = element.Mappings.Hl7v251;

            if (!segment.Name.Equals("OBX") || mapping.SegmentType != SegmentType.OBX)
            {
                // don't bother validating OBX data types for non-OBX segments
                return validationMessages;
            }

            var mismatch = segment.Fields(2).Value != mapping.DataType.ToString();

            if (mismatch)
            {
                var validationMessage = new ValidationMessage(
                    severity: Severity.Warning,
                    messageType: ValidationMessageType.Structural,
                    content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is specified as type {mapping.DataType}, but appears in the HL7 message as type {segment.Fields(2).Value}",
                    path: $"OBX[{segment.Fields(1).Value}].2",
                    pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                validationMessages.Add(validationMessage);
            }

            return validationMessages;
        }
    }
}
