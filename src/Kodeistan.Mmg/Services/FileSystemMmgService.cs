using Kodeistan.Mmg.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kodeistan.Mmg.Services
{
    public class FileSystemMmgService : IMmgService
    {
        private readonly Dictionary<string, MessageMappingGuide> _mmgs = new Dictionary<string, MessageMappingGuide>();

        public MessageMappingGuide Get(string profileIdentifier, string conditionCode)
        {
            MessageMappingGuide mmg = new MessageMappingGuide();

            if (_mmgs.TryGetValue(profileIdentifier, out mmg))
            {
                return mmg;
            }
            else
            {
                string mmgFilename = GetMmgFileName(profileIdentifier);
                var mmgJsonString = File.ReadAllText(Path.Combine("mmgs", mmgFilename));

                mmg = JsonConvert.DeserializeObject<MessageMappingGuide>(mmgJsonString);

                _mmgs.Add(profileIdentifier, mmg);

                return mmg;
            }
        }

        private string GetMmgFileName(string profileIdentifier)
        {
            return profileIdentifier switch
            {
                string mmg when mmg.Equals("NOTF_ORU_v3.0^PHINProfileID^2.16.840.1.114222.4.10.3^ISO~Generic_MMG_V2.0^PHINMsgMapID^2.16.840.1.114222.4.10.4^ISO") => "genv2.json",
                string mmg when mmg.Contains("Pertussis_MMG") => "pertussis_1.0.json",
                string mmg when mmg.Contains("CongenitalSyphilis_MMG_V1") => "cs_1.0.json",
                string mmg when mmg.Contains("Varicella_MMG_V3.0") => "varicella_3.0.json",
                string mmg when mmg.Contains("Trichinellosis_MMG_V1.0") => "trichinellosis_1.0.json",
                string mmg when mmg.Contains("Lyme_TBRD_MMG_V1.0") => "lyme_tbrd_1.0.json",
                string mmg when mmg.Contains("TB_MMG_V3.0") => "tb_3.0.json",
                string mmg when mmg.Contains("STD_MMG_V1.0") => "std_1.0.json",
                string mmg when mmg.Contains("STD_MMG_V1.1") => "std_1.1.json",

                string mmg when mmg.Contains("Malaria_MMG_V1.0") => "malaria_1.0.json",
                string mmg when mmg.Contains("CongenitalSyphilis_MMG_V1.0") => "cs_1.0.json",
                string mmg when mmg.Contains("Mumps_MMG_V1.0") => "mumps_1.0.json",
            };
        }
    }
}
