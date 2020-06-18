using Kodeistan.Mmg.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kodeistan.Mmg.Services
{
    public class FileSystemMmgService : IMmgService
    {
        public MessageMappingGuide Get(string profileIdentifier)
        {
            string mmgFilename = GetMmgFileName(profileIdentifier);

            var mmgJsonString = File.ReadAllText(Path.Combine("mmgs", mmgFilename));

            MessageMappingGuide mmg = JsonConvert.DeserializeObject<MessageMappingGuide>(mmgJsonString);

            return mmg;
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        private string GetMmgFileName(string profileIdentifier)
        {
            return profileIdentifier switch
            {
                string mmg when mmg.Equals("NOTF_ORU_v3.0^PHINProfileID^2.16.840.1.114222.4.10.3^ISO~Generic_MMG_V2.0^PHINMsgMapID^2.16.840.1.114222.4.10.4^ISO") => "genv2.json",
                string mmg when mmg.Contains("Pertussis_MMG") => "pertussis.json",
                string mmg when mmg.Contains("Malaria_MMG") => "malaria.json",
                string mmg when mmg.Contains("CongenitalSyphilis_MMG_V1") => "cs_1.0.json",

                string mmg when mmg.Contains("Varicella_MMG_V3.0") => "varicella.json",
                string mmg when mmg.Contains("Trichinellosis_MMG_V1.0") => "trichinellosis.json",
                string mmg when mmg.Contains("Lyme_TBRD_MMG_V1.0") => "tbrd.json",
                string mmg when mmg.Contains("TB_MMG_V3.0") => "Tuberculosis and LTBI.json",
                string mmg when mmg.Contains("STD_MMG_V1.0") => "std.json",
                string mmg when mmg.Contains("STD_MMG_V1.1") => "std_1.1.json",
            };
        }
    }
}
