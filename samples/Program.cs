using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kodeistan.Mmg;
using Kodeistan.Mmg.Model;
using Kodeistan.Mmg.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Kodeistan.Mmg.Samples
{
    class Program
    {
        static void Main(string[] args)
        {
            string fileName = "STD_V1_0_TM_TC01.txt";
            string hl7v2message = File.ReadAllText(Path.Combine("data", fileName));

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            IVocabularyService vocabService = new FakeVocabularyService();
            IMmgService mmgService = new InMemoryMmgService();

            Validator validator = new Validator(vocabService, mmgService);

            var result = validator.ValidateMessage(hl7v2message);

            foreach (var validationMessage in result.ValidationMessages)
            {
                Console.WriteLine($"{fileName} : {validationMessage.MessageType} : {validationMessage.Path} : {validationMessage.Content}");
            }

            Console.WriteLine();

            Console.WriteLine($"{result.Errors} errors, {result.Warnings} warnings, {result.ValidationMessages.Where(r => r.Severity == Severity.Information).Count()} information. Elapsed time: {sw.Elapsed.TotalMilliseconds.ToString("N0")} ms. Validation CPU time: {result.Elapsed.TotalMilliseconds.ToString("N0")}");
        }
    }
}
