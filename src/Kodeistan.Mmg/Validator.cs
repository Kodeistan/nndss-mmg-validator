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
            _vocabService = new InMemoryVocabularyService();
            _mmgService = new InMemoryMmgService();
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
            else if (
                mshSegment.GetAllFields().Count < 21 || 
                string.IsNullOrEmpty(mshSegment.Fields(21).Value) ||
                !mshSegment.Fields(21).Repetitions(1).Value.Equals("NOTF_ORU_v3.0^PHINProfileID^2.16.840.1.114222.4.10.3^ISO") ||
                !mshSegment.Fields(21).Repetitions(2).Value.Equals("Generic_MMG_V2.0^PHINMsgMapID^2.16.840.1.114222.4.10.4^ISO"))
            {
                var msh21error = new ValidationMessage(
                        Severity.Error,
                        ValidationMessageType.Structural,
                        $"An unsupported literal value was provided for the Message Profile Identifier (NOT115, MSH-21). Replace the unsupported literal value with the required literal value as defined in the conformance statement within the current version of the PHIN Messaging Guide for Case Notification Reporting and the appropriate Message Mapping Guide.",
                        $"MSH[1].21");

                msh21error.ErrorCode = "00025";

                validationMessages.Add(msh21error);

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
            // check for unique OBX-3 and OBX-4 values
            validationMessages.AddRange(ValidateOBXUniqueness(message));

            validationMessages.AddRange(ValidateBusinessRules(message, messageMappingGuide));

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

        public List<ValidationMessage> ValidateOBXUniqueness(Message message)
        {
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();
            var segments = message.Segments();

            HashSet<string> uniqueValues = new HashSet<string>();

            bool notUnique = false;
            foreach (var segment in segments)
            {
                if (segment.Name == "OBR")
                {
                    notUnique = false;
                }
                
                if (!segment.Name.Equals("OBX"))
                {
                    continue;
                }

                var obx1 = segment.Fields(1).Value;

                var obx3 = segment.Fields(3).Value;
                var obx4 = segment.Fields(4).Value;

                var uniqueValue = $"{obx3}_{obx4}";

                if (uniqueValues.Contains(uniqueValue))
                {
                    var uniquenessConstraintViolationMessage = new ValidationMessage(
                            severity: Severity.Error,
                            messageType: ValidationMessageType.Content,
                            content: $"The combination of the data element identifier (OBX-3) and the observation sub id (OBX-4) must be unique.",
                            path: $"OBX[{obx1}].3",
                            pathAlternate: $"OBX[{obx1}].4");
                    uniquenessConstraintViolationMessage.ErrorCode = "00003";

                    validationMessages.Add(uniquenessConstraintViolationMessage);

                    break;
                }
                else
                {
                    uniqueValues.Add(uniqueValue);
                }
            }

            return validationMessages;
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
                            var requiredMessage = new ValidationMessage(
                                Severity.Error,
                                ValidationMessageType.Content, 
                                $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is required, but was not found in the message", 
                                $"OBX[{segment.Fields(1).Value}]");

                            requiredMessage.ErrorCode = "00012";

                            validationMessages.Add(requiredMessage);
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
                            var requiredMessage = new ValidationMessage(
                                Severity.Error,
                                ValidationMessageType.Content, 
                                $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is required, but was not found in the message",
                                $"OBX[{relatedSegment.Fields(1).Value}].{pos}");

                            requiredMessage.ErrorCode = "00012";

                            validationMessages.Add(requiredMessage);
                        }

                        break;
                    }
                }

                if (!found)
                {
                    var requiredMessage = new ValidationMessage(
                        Severity.Error,
                        ValidationMessageType.Content,
                        $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is required, but was not found in the message", 
                        $"");

                    requiredMessage.ErrorCode = "00012";

                    validationMessages.Add(requiredMessage);
                }
            }

            return validationMessages;
        }

        private List<ValidationMessage> ValidateBusinessRules(Message message, MessageMappingGuide messageMappingGuide)
        {
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();

            var pidSegment = message.Segments("PID").FirstOrDefault();
            // TODO: Check that PID exists? This should have been checked elsewhere, but perhaps we should do that here too in case the function order changes?

            var birthdateField = pidSegment.Fields(7);
            var birthdate = ConvertHL7Timestamp(birthdateField.Value);

            #region 00063 - Date of illness onset is NOT NULL and earlier than birthdate
            var illnessOnsetSegment = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("11368-8") || s.Fields(3).Components(1).Value.Equals("INV137"))
                .FirstOrDefault();

            var illnessOnsetField = illnessOnsetSegment?.Fields(5);

            if (illnessOnsetField != null && birthdateField != null && !string.IsNullOrEmpty(illnessOnsetField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var illnessOnset = ConvertHL7Timestamp(illnessOnsetField.Value);

                if (illnessOnset < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV137 (Date of Illness Onset) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00063";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00064 - Date of illness end date occurs before date of illness onset
            var illnessEndSegment = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77976-9") || s.Fields(3).Components(1).Value.Equals("INV138"))
                .FirstOrDefault();

            var illnessEndField = illnessEndSegment?.Fields(5);

            if (illnessEndField != null && illnessOnsetField != null && !string.IsNullOrEmpty(illnessEndField.Value) && !string.IsNullOrEmpty(illnessOnsetField.Value))
            {
                var illnessOnset = ConvertHL7Timestamp(illnessOnsetField.Value);
                var ilnessEnd = ConvertHL7Timestamp(illnessEndField.Value);

                if (ilnessEnd < illnessOnset)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV138 (Illness End Date) is earlier than INV137 (Date of Illness Onset)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00064";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00065 - Date of illness onset is NOT NULL and earlier than birthdate
            if (illnessEndField != null && birthdateField != null && !string.IsNullOrEmpty(illnessEndField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var illnessEnd = ConvertHL7Timestamp(illnessEndField.Value);

                if (illnessEnd < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV138 (Illness End Date) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00065";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00067 - Pregnant Male business rule
            var patientSex = pidSegment.Fields(8).Value;
            var pregancyStatus = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77996-7"))
                .FirstOrDefault();

            if (pregancyStatus != null && pregancyStatus.Value.Equals("Y") && patientSex.Equals("M"))
            {
                var ruleViolationMessage = new ValidationMessage(
                    severity: Severity.Warning,
                    messageType: ValidationMessageType.Rule,
                    content: $"77996-7 (Pregnancy Status) was addressed where DEM113 (Subject's Sex) is Male",
                    path: $"");

                ruleViolationMessage.ErrorCode = "00067";

                validationMessages.Add(ruleViolationMessage);
            }
            #endregion

            #region 00069 - Date of diagnosis is NOT NULL and occurs before birthdate
            var diagnosisDateField = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77975-1") || s.Fields(3).Components(1).Value.Equals("INV136"))
                .FirstOrDefault()
                ?.Fields(5);

            if (diagnosisDateField != null && birthdateField != null && !string.IsNullOrEmpty(diagnosisDateField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var diagnosisDate = ConvertHL7Timestamp(diagnosisDateField.Value);

                if (diagnosisDate < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV136 (Diagnosis Date) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00069";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00072 - Date of admission to hospital is NOT NULL and occurs before birthdate
            var admissionDateField = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("8656-1") || s.Fields(3).Components(1).Value.Equals("INV132"))
                .FirstOrDefault()
                ?.Fields(5);

            if (admissionDateField != null && birthdateField != null && !string.IsNullOrEmpty(admissionDateField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var admissionDate = ConvertHL7Timestamp(admissionDateField.Value);

                if (admissionDate < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV132 (Admission Date) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00072";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00073 - Date of discharge is NOT NULL and occurs before birthdate
            var dischargeDateField = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("8649-6") || s.Fields(3).Components(1).Value.Equals("INV133"))
                .FirstOrDefault()
                ?.Fields(5);

            if (dischargeDateField != null && birthdateField != null && !string.IsNullOrEmpty(dischargeDateField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var dischargeDate = ConvertHL7Timestamp(dischargeDateField.Value);

                if (dischargeDate < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV133 (Discharge Date) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00073";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00079 - Date of death is NOT NULL and occurs before birthdate
            var deceasedDateField = pidSegment.Fields(29);

            if (deceasedDateField != null && birthdateField != null && !string.IsNullOrEmpty(deceasedDateField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var deceasedDate = ConvertHL7Timestamp(deceasedDateField.Value);

                if (deceasedDate < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV146 (Deceased Date) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00079";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00101 - Earliest date reported to the county is NOT NULL and occurs before birthdate
            var earliestDateReportedToCountyField = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77972-8") || s.Fields(3).Components(1).Value.Equals("INV120"))
                .FirstOrDefault()
                ?.Fields(5);

            if (earliestDateReportedToCountyField != null && birthdateField != null && !string.IsNullOrEmpty(earliestDateReportedToCountyField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var earliestDateReportedToCounty = ConvertHL7Timestamp(earliestDateReportedToCountyField.Value);

                if (earliestDateReportedToCounty < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV120 (Earliest Date Reported to County) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00101";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00102 - Earliest date reported to the state is NOT NULL and occurs before birthdate
            var earliestDateReportedToStateField = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77973-6") || s.Fields(3).Components(1).Value.Equals("INV121"))
                .FirstOrDefault()
                ?.Fields(5);

            if (earliestDateReportedToStateField != null && birthdateField != null && !string.IsNullOrEmpty(earliestDateReportedToStateField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var earliestDateReportedToState = ConvertHL7Timestamp(earliestDateReportedToStateField.Value);

                if (earliestDateReportedToState < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV121 (Earliest Date Reported to State) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00102";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00103 - MMWR week is valid
            var mmwrWeekValue = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77991-8") || s.Fields(3).Components(1).Value.Equals("INV165"))
                .FirstOrDefault()
                ?.Fields(5)
                ?.Components(2)
                ?.Value;

            if (!string.IsNullOrWhiteSpace(mmwrWeekValue) && int.TryParse(mmwrWeekValue, out int mmwrWeek) && (mmwrWeek < 1 || mmwrWeek > 53))
            {   
                var ruleViolationMessage = new ValidationMessage(
                    severity: Severity.Error,
                    messageType: ValidationMessageType.Rule,
                    content: $"INV165 (MMWR Week) is not populated with an integar between 1 and 53.",
                    path: $"");

                ruleViolationMessage.ErrorCode = "00103";

                validationMessages.Add(ruleViolationMessage);
            }
            #endregion

            #region 00105 - Earliest date reported to the CDC is NOT NULL and occurs before birthdate
            var earliestDateReportedToCDCField = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77994-2") || s.Fields(3).Components(1).Value.Equals("INV176"))
                .FirstOrDefault()
                ?.Fields(5);

            if (earliestDateReportedToCDCField != null && birthdateField != null && !string.IsNullOrEmpty(earliestDateReportedToCDCField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var earliestDateReportedToCDC = ConvertHL7Timestamp(earliestDateReportedToCDCField.Value);

                if (earliestDateReportedToCDC < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV176 (Date CDC was first verbally notified of this Case) is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00105";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            #region 00106 - Earliest date reported to the PHD is NOT NULL and occurs before birthdate
            var earliestDateReportedToPHDField = message.Segments("OBX")
                .Where(s => s.Fields(3).Components(1).Value.Equals("77970-2") || s.Fields(3).Components(1).Value.Equals("INV177"))
                .FirstOrDefault()
                ?.Fields(5);

            if (earliestDateReportedToPHDField != null && birthdateField != null && !string.IsNullOrEmpty(earliestDateReportedToPHDField.Value) && !string.IsNullOrEmpty(birthdateField.Value))
            {
                var earliestDateReportedToPHD = ConvertHL7Timestamp(earliestDateReportedToPHDField.Value);

                if (earliestDateReportedToPHD < birthdate)
                {
                    var ruleViolationMessage = new ValidationMessage(
                        severity: Severity.Warning,
                        messageType: ValidationMessageType.Rule,
                        content: $"INV177 (Date First Reported PHD) on is earlier than DEM115 (Patient Date of Birth)",
                        path: $"");

                    ruleViolationMessage.ErrorCode = "00106";

                    validationMessages.Add(ruleViolationMessage);
                }
            }
            #endregion

            return validationMessages;
        }

        public DateTime ConvertHL7DateTime(string hl7DateTime)
        {
            // TODO: Convert to static helper function?

            string format = "yyyyMMdd";
            DateTime dateTime = DateTime.ParseExact(hl7DateTime, format,
                CultureInfo.InvariantCulture);
            return dateTime;
        }

        public DateTimeOffset ConvertHL7Timestamp(string hl7Timestamp)
        {
            // TODO: Convert to static helper function?

            string format = "yyyyMMddHHmmss.ffff".Substring(0, hl7Timestamp.Length);
            DateTimeOffset dateTime = DateTimeOffset.ParseExact(hl7Timestamp, format,
                CultureInfo.InvariantCulture);
            return dateTime;
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
                    var unexpectedObxSegment = new ValidationMessage(
                        Severity.Warning,
                        ValidationMessageType.Structural,
                        $"Data was found in an OBX segment with identifier '{identifier}', but no data elements corresponding with this identifier were found in the '{messageMappingGuide.Name}' message mapping guide.",
                        $"OBX[{segment.Fields(1).Value}].3");

                    unexpectedObxSegment.ErrorCode = "00010";

                    validationMessages.Add(unexpectedObxSegment);
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
                    #region Check the OBX-3 values to ensure they match what's in the MMG
                    // do a check to make sure the OBX-3.2 and OBX-3.3 values are correct
                    string obx32 = segment.Fields(3).Components(2).Value;
                    string obx33 = segment.Fields(3).Components(3).Value;

                    if (!element.Name.Equals(obx32, StringComparison.OrdinalIgnoreCase))
                    {
                        var message = new ValidationMessage(
                            severity: Severity.Warning,
                            messageType: ValidationMessageType.Content,
                            content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' has an unexpected value in OBX-3.2. Expected: {element.Name}. Actual: {obx32}.",
                            path: $"OBX[{segment.Fields(1).Value}].3.2",
                            pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                        validationMessages.Add(message);
                    }

                    if (element.CodeSystem.HasValue && !element.CodeSystem.Value.ToString().Equals(obx33, StringComparison.OrdinalIgnoreCase))
                    {
                        // Note: Added this check from OT's wishlist

                        var message = new ValidationMessage(
                            severity: Severity.Warning,
                            messageType: ValidationMessageType.Content,
                            content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' has an unexpected value in OBX-3.3. Expected: {element.CodeSystem.Value}. Actual: {obx33}.",
                            path: $"OBX[{segment.Fields(1).Value}].3.3",
                            pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                        validationMessages.Add(message);
                    }
                    #endregion

                    #region Check that repeating elements have values in OBX-4
                    if (mapping.RepeatingGroupElementType != RepeatingGroupElementType.No)
                    {
                        var obx4 = segment.Fields(4).Value;
                        if (string.IsNullOrWhiteSpace(obx4))
                        {
                            var missingObx4Message = new ValidationMessage(
                                severity: Severity.Error,
                                messageType: ValidationMessageType.Content,
                                content: $"OBX-4 (Observation Sub-ID) MUST be populated for data elements in a repeating group. Data element '{element.Name}' with identifier '{mapping.Identifier}' is missing an OBX-4 value.",
                                path: $"OBX[{segment.Fields(1).Value}].4",
                                pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                            missingObx4Message.ErrorCode = "00007";
                            missingObx4Message.DataElementId = element.Id.ToString();

                            validationMessages.Add(missingObx4Message);
                        }
                    }
                    #endregion


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

                                var vocabularyResult = _vocabService.IsValid(conceptCode, "", "", relatedElement.ValueSetCode);

                                if (!vocabularyResult.IsCodeValid)
                                {
                                    List<ValidationMessage> vocabularyMessages = BuildInvalidVocabularyMessages(
                                        vocabularyResult,
                                        relatedElement,
                                        segment,
                                        path: $"OBX[{segment.Fields(1).Value}].{relatedElement.Mappings.Hl7v251.FieldPosition.Value}",
                                        pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                    //ValidationMessage illegalConceptCodeMessage = new ValidationMessage(
                                    //    severity: Severity.Warning,
                                    //    messageType: ValidationMessageType.Structural,
                                    //    content: $"Data element '{relatedElement.Name}' in with identifier '{relatedElement.Mappings.Hl7v251.Identifier}' is a coded element associated with the value set '{relatedElement.ValueSetCode}'. However, the concept code '{conceptCode}' in the message was not found as a valid concept for this value set",
                                    //    path: $"OBX[{segment.Fields(1).Value}].{relatedElement.Mappings.Hl7v251.FieldPosition.Value}",
                                    //    pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                    validationMessages.AddRange(vocabularyMessages);
                                }
                            }
                        }
                    }

                    var dataTypeValidationMessages = ValidateOBXDataType(segment, element);
                    validationMessages.AddRange(dataTypeValidationMessages);

                    var obx5 = segment.Fields(5);

                    if (obx5.HasRepetitions && element.Repetitions.HasValue && element.Repetitions <= 1)
                    {
                        // element is defined as not repeating, but has repeats in a message
                        ValidationMessage illegalRepeatsMessage = new ValidationMessage(
                            severity: Severity.Warning,
                            messageType: ValidationMessageType.Structural,
                            content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is not a repeating element, but has repeating data in the message",
                            path: $"OBX[{segment.Fields(1).Value}].5",
                            pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                        illegalRepeatsMessage.ErrorCode = "0001";
                        illegalRepeatsMessage.DataElementId = element.Id.ToString();

                        validationMessages.Add(illegalRepeatsMessage);
                    }
                    else if (obx5.HasRepetitions && element.Repetitions.HasValue && obx5.Repetitions().Count > element.Repetitions)
                    {
                        var maxAllowedRepeats = element.Repetitions.Value;
                        var actualRepeats = segment.Fields(5).Repetitions().Count.ToString();

                        // element is repeating, but has too many repeats
                        ValidationMessage illegalRepeatsMessage = new ValidationMessage(
                            severity: Severity.Warning,
                            messageType: ValidationMessageType.Structural,
                            content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' has too many repeats. Maximum allowed: {maxAllowedRepeats}. Actual: {actualRepeats}.",
                            path: $"OBX[{segment.Fields(1).Value}].5",
                            pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                        illegalRepeatsMessage.ErrorCode = "0002";
                        illegalRepeatsMessage.DataElementId = element.Id.ToString();

                        validationMessages.Add(illegalRepeatsMessage);
                    }

                    // check coded elements
                    string dataType = segment.Fields(2).Value;
                    if ( (dataType.Equals("CWE") && mapping.DataType == Model.HL7V251.DataType.CWE) || (dataType.Equals("CE") && mapping.DataType == Model.HL7V251.DataType.CE) )
                    {
                        string valueSetCode = element.ValueSetCode;

                        if (obx5.HasRepetitions)
                        {
                            for (int i = 0; i < obx5.Repetitions().Count; i++)
                            {
                                var repetition = obx5.Repetitions()[i];
                                
                                string conceptCode = repetition.Components(1).Value;
                                string conceptName = repetition.Components(2).Value;
                                string conceptCodeSystem = repetition.Components(3).Value;
                                var vocabularyResult = _vocabService.IsValid(conceptCode, conceptName, conceptCodeSystem, valueSetCode);

                                if (!vocabularyResult.IsCodeValid)
                                {
                                    List<ValidationMessage> vocabularyMessages = BuildInvalidVocabularyMessages(
                                        vocabularyResult,
                                        element,
                                        segment,
                                        path: $"OBX[{segment.Fields(1).Value}].5[{i + 1}]",
                                        pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                    //new ValidationMessage(
                                    //severity: Severity.Warning,
                                    //messageType: ValidationMessageType.Vocabulary,
                                    //content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is a coded element associated with the value set '{valueSetCode}'. However, the concept code '{conceptCode}' in the message was not found as a valid concept for this value set in repetition {i + 1}",
                                    //path: $"OBX[{segment.Fields(1).Value}].5[{i + 1}]",
                                    //pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                    validationMessages.AddRange(vocabularyMessages);
                                }
                            }
                        }
                        else
                        {
                            // it's a coded element, so check for vocabulary
                            string conceptCode = segment.Fields(5).Components(1).Value;
                            string conceptName = segment.Fields(5).Components(2).Value;
                            string conceptCodeSystem = segment.Fields(5).Components(3).Value;
                            var vocabularyResult = _vocabService.IsValid(conceptCode, conceptName, conceptCodeSystem, valueSetCode);

                            if (!vocabularyResult.IsCodeValid)
                            {
                                List<ValidationMessage> vocabularyMessages = BuildInvalidVocabularyMessages(
                                        vocabularyResult,
                                        element, 
                                        segment, 
                                        path: $"OBX[{segment.Fields(1).Value}].5", 
                                        pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                    //new ValidationMessage(
                                    //severity: Severity.Warning,
                                    //messageType: ValidationMessageType.Vocabulary,
                                    //content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is a coded element associated with the value set '{valueSetCode}'. However, the concept code '{conceptCode}' in the message was not found as a valid concept for this value set",
                                    //path: $"OBX[{segment.Fields(1).Value}].5",
                                    //pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                                validationMessages.AddRange(vocabularyMessages);
                            }
                        }
                    }

                    // check date elements
                    if (dataType.Equals("DT") || dataType.Equals("TS"))
                    {
                        if (obx5.Value.Equals("99999999"))
                        {
                            var illegalDateValue = new ValidationMessage(
                                severity: Severity.Error,
                                messageType: ValidationMessageType.Content,
                                content: $"{mapping.Identifier} ({element.Name}) is a required date data element and can’t be populated with '99999999'",
                                path: $"OBX[{segment.Fields(1).Value}].5",
                                pathAlternate: $"OBX[{segment.Fields(1).Value}].3.1");

                            illegalDateValue.ErrorCode = "00008";
                            illegalDateValue.DataElementId = element.Id.ToString();

                            validationMessages.Add(illegalDateValue);
                        }
                    }

                    instance++; // TODO: Add instance number to the path string, e.g. if this OBX shows up multiple times (as it might with repeating groups)
                }
            }

            return validationMessages;
        }

        private List<ValidationMessage> BuildInvalidVocabularyMessages(VocabularyValidationResult vocabularyResult, DataElement element, Segment segment, string path, string pathAlternate)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();

            var mapping = element.Mappings.Hl7v251;
            string valueSetCode = element.ValueSetCode;

            if (!vocabularyResult.IsCodeValid)
            {
                ValidationMessage illegalConceptCodeMessage = new ValidationMessage(
                    severity: Severity.Warning,
                    messageType: ValidationMessageType.Vocabulary,
                    content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is a coded element associated with the value set '{valueSetCode}'. However, the concept code '{vocabularyResult.ConceptCode}' in the message was not found as a valid concept for this value set",
                    path: path,
                    pathAlternate: pathAlternate);

                illegalConceptCodeMessage.DataElementId = element.Id.ToString();
                illegalConceptCodeMessage.ValueSetCode = valueSetCode;

                messages.Add(illegalConceptCodeMessage);
            }
            if (vocabularyResult.IsCodeValid && !vocabularyResult.IsNameValid)
            {
                ValidationMessage illegalConceptNameMessage = new ValidationMessage(
                    severity: Severity.Warning,
                    messageType: ValidationMessageType.Vocabulary,
                    content: $"Data element '{element.Name}' with identifier '{mapping.Identifier}' is a coded element associated with the value set '{valueSetCode}'. However, the concept name '{vocabularyResult.ConceptName}' in the message was not found as a valid concept name for the code '{vocabularyResult.ConceptCode}' in this value set",
                    path: path,
                    pathAlternate: pathAlternate);

                illegalConceptNameMessage.DataElementId = element.Id.ToString();
                illegalConceptNameMessage.ValueSetCode = valueSetCode;

                messages.Add(illegalConceptNameMessage);
            }

            return messages;
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
