using Kodeistan.Mmg.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kodeistan.Mmg.Tests
{
    public class ValidatorFixture : IDisposable
    {
        public Validator Validator { get; set; }

        public ValidatorFixture()
        {
            Validator = BuildValidator();
        }

        private Validator BuildValidator()
        {
            IMmgService mmgService = new FileSystemMmgService();
            IVocabularyService vocabService = new InMemoryVocabularyService();

            Validator validator = new Validator(
                vocabService: vocabService,
                mmgService: mmgService);

            return validator;
        }

        private void LoadMmgs()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;

            var path = Path.Combine(basePath, "mmgs");

            DirectoryInfo dir = new DirectoryInfo(path);

            var jsonFiles = dir.GetFiles("*.json");

            foreach (var jsonFile in jsonFiles)
            {
                var json = File.ReadAllText(jsonFile.FullName);
            }
        }

        public void Dispose()
        {
        }
    }
}
