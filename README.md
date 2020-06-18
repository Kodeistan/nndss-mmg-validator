# Validator for NNDSS Message Mapping Guides

The purpose of this C# library is to detect content-based validation errors with messages received by the CDC through the National Notifiable Diseases Surveillance System. _This library is a prototype and not intended for production use._

There are around 80 "notifiable conditions" that the U.S. states report to the CDC.

Validation will occur based on two inputs: 
1. A structurally valid HL7 message (or Json representation of the HL7 message)
1. A Json representation of the MMG

An MMG is a Json document. It contains a list of "data elements" that U.S. states send to the CDC, as well as metadata about those elements, such as their data type, allowed maximum length, and what valid values are allowed for that element. Importantly, the MMG contains metadata about how to map those data elements to the pipe-delimited HL7 file format.

There is one MMG per "notifiable condition". A "notifiable condition" is a public health concern that the states are encourged to report to the CDC. For instance, if a person is diagnosed with Zika, their case may be reported by that state to the CDC via NNDSS. Thus, an MMG must be created to tell states how to map Zika-specific data elements to an HL7 message.

MMGs can be downloaded from https://wwwn.cdc.gov/nndss/case-notification/message-mapping-guides.html in Excel format.

## Runtimes

The validator is implemented as a basic C# class and targets the .NET Standard 2.0 API. As such, it will run in a Linux container, on macOS, and on Windows.

## Using the validator in a .NET Core 3 app

In a scenario where the conversion is taking place in an AWS Lambda, Azure function, or ASP.NET Core microservice, the target runtime is likely to be Linux and thus .NET Core (or .NET 5) will be the target framework. To use this library in such a case, simply add the following to your `.csproj` file:

```xml
<PackageReference Include="Kodeistan.Mmg" Version="0.0.2" />
```

The library can then be used like this:

```cs
using Kodeistan.Mmg;
using Kodeistan.Mmg.Model;

var validator = new Validator();
string hl7v2message = ... // get raw text content of the HL7 v2.5.1 ORU_R01 message and assign it to this string variable

ValidationResult result = validator.ValidateMessage(hl7v2message);

foreach (ValidationMessage validationMessage in results.ValidationMessages)
{
    Console.WriteLine($"{fileName} : {result.MessageType} : {result.Path} : {result.Content}");
}
```

## Supplying custom vocabulary and MMG services

By default, no vocabulary validation is carried out, as the specifics of talking to a vocabulary service can vary from system to system. Instead, this library enables developers to inject their own vocabulary service into the validator via dependency injection. Simply implement the `IVocabularyService` interface and pass it the `Validator` constructor.

The `Validator` also needs a machine-readable message mapping guide and an interface for this is provided: `IMmgService`. Both of these interfaces have fake/in-memory implementations for testing purposes, but developers consuming this library are encouraged to implement their own concrete realizations of these interfaces that work for their use cases.

```cs
IVocabularyService vocabService =  new VocabularyService(); // your vocabulary service implementation
IMmgServce mmgService =  new HttpMmgService(); // your Mmg retriver service implementation
Validator validator = new Validator(vocabService, mmgService);
```

## Using the validator from the terminal

A sample console application is provided in the `samples` folder that shows how the library can be used. To run the example application, navigate to the `samples` folder in this repo and then execute the following command in a terminal window:

```sh
dotnet run
```

The .NET code in this repo will run on macOS, Linux, and Windows, as long as you have the [.NET SDK](https://dotnet.microsoft.com/download).

