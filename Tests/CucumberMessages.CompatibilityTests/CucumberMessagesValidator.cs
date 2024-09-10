﻿using FluentAssertions;
using Reqnroll.CucumberMessages;
using Io.Cucumber.Messages.Types;
using System.ComponentModel.Design;
using FluentAssertions.Execution;
using System.Reflection;

namespace CucumberMessages.CompatibilityTests
{
    public class CucumberMessagesValidator
    {
        private IEnumerable<Envelope> actualEnvelopes;
        private IEnumerable<Envelope> expectedEnvelopes;

        // cross-reference metadata
        private Dictionary<Type, HashSet<string>> actuals_IDsByType = new();
        private Dictionary<Type, HashSet<string>> expecteds_IDsByType = new();
        private Dictionary<Type, HashSet<object>> actuals_elementsByType = new();
        private Dictionary<Type, HashSet<object>> expecteds_elementsByType = new();
        private Dictionary<string, HashSet<object>> actuals_elementsByID = new();
        private Dictionary<string, HashSet<object>> expecteds_elementsByID = new();
        private readonly FluentAsssertionCucumberMessagePropertySelectionRule FA_CustomCucumberMessagesPropertySelector;

        // Envelope types - these are the top level types in CucumberMessages
        // Meta is excluded from the list as there is nothing there for us to compare
        private readonly IEnumerable<Type> EnvelopeTypes = new Type[] { typeof(Attachment), typeof(GherkinDocument), typeof(Hook), typeof(ParameterType), typeof(Source),
                                                                        typeof(StepDefinition), typeof(TestCase), typeof(TestCaseFinished), typeof(TestCaseStarted), typeof(TestRunFinished),
                                                                        typeof(TestRunStarted), typeof(TestStepFinished), typeof(TestStepStarted), typeof(UndefinedParameterType) };

        public CucumberMessagesValidator(IEnumerable<Envelope> actual, IEnumerable<Envelope> expected)
        {
            actualEnvelopes = actual;
            expectedEnvelopes = expected;

            SetupCrossReferences(actual, actuals_IDsByType, actuals_elementsByType, actuals_elementsByID);
            SetupCrossReferences(expected, expecteds_IDsByType, expecteds_elementsByType, expecteds_elementsByID);

            FA_CustomCucumberMessagesPropertySelector = new FluentAsssertionCucumberMessagePropertySelectionRule(expecteds_elementsByType.Keys.ToList());
            AssertionOptions.AssertEquivalencyUsing(options => options
                                                                    // invoking these for each Type in CucumberMessages so that FluentAssertions DOES NOT call .Equal wwhen comparing instances
                                                                    .ComparingByValue<Attachment>()
                                                                    .ComparingByMembers<Background>()
                                                                    .ComparingByMembers<Ci>()
                                                                    .ComparingByMembers<Comment>()
                                                                    .ComparingByMembers<DataTable>()
                                                                    .ComparingByMembers<DocString>()
                                                                    .ComparingByMembers<Envelope>()
                                                                    .ComparingByMembers<Examples>()
                                                                    .ComparingByMembers<Feature>()
                                                                    .ComparingByMembers<FeatureChild>()
                                                                    .ComparingByMembers<GherkinDocument>()
                                                                    .ComparingByMembers<Group>()
                                                                    .ComparingByMembers<Hook>()
                                                                    .ComparingByMembers<Location>()
                                                                    .ComparingByMembers<Meta>()
                                                                    .ComparingByMembers<ParameterType>()
                                                                    .ComparingByMembers<ParseError>()
                                                                    .ComparingByMembers<Pickle>()
                                                                    .ComparingByMembers<PickleDocString>()
                                                                    .ComparingByMembers<PickleStep>()
                                                                    .ComparingByMembers<PickleStepArgument>()
                                                                    .ComparingByMembers<PickleTable>()
                                                                    .ComparingByMembers<PickleTableCell>()
                                                                    .ComparingByMembers<PickleTableRow>()
                                                                    .ComparingByMembers<PickleTag>()
                                                                    .ComparingByMembers<Product>()
                                                                    .ComparingByMembers<Rule>()
                                                                    .ComparingByMembers<RuleChild>()
                                                                    .ComparingByMembers<Scenario>()
                                                                    .ComparingByMembers<Source>()
                                                                    .ComparingByMembers<SourceReference>()
                                                                    .ComparingByMembers<Step>()
                                                                    .ComparingByMembers<StepDefinition>()
                                                                    .ComparingByMembers<StepDefinitionPattern>()
                                                                    .ComparingByMembers<StepMatchArgument>()
                                                                    .ComparingByMembers<StepMatchArgumentsList>()
                                                                    .ComparingByMembers<TableCell>()
                                                                    .ComparingByMembers<TableRow>()
                                                                    .ComparingByMembers<Tag>()
                                                                    .ComparingByMembers<TestCase>()
                                                                    .ComparingByMembers<TestCaseFinished>()
                                                                    .ComparingByMembers<TestCaseStarted>()
                                                                    .ComparingByMembers<TestRunFinished>()
                                                                    .ComparingByMembers<TestRunStarted>()
                                                                    .ComparingByMembers<TestStep>()
                                                                    .ComparingByMembers<TestStepFinished>()
                                                                    .ComparingByMembers<TestStepResult>()
                                                                    .ComparingByMembers<TestStepStarted>()
                                                                    .ComparingByMembers<UndefinedParameterType>()

                                                                    // Using a custom Property Selector so that we can ignore the  properties that are not comparable
                                                                    .Using(FA_CustomCucumberMessagesPropertySelector)

                                                                   // Using a custom string comparison to ignore the differences in platform line endings
                                                                   .Using<string>((ctx) =>
                                                                       {
                                                                           var subject = ctx.Subject ?? string.Empty;
                                                                           var expectation = ctx.Expectation ?? string.Empty;
                                                                           subject = subject.Replace("\r\n", "\n");
                                                                           expectation = expectation.Replace("\r\n", "\n");
                                                                           subject.Should().Be(expectation);
                                                                       })
                                                                    .WhenTypeIs<string>()

                                                                    // A bit of trickery here to tell FluentAssertions that Timestamps are always equal
                                                                    // We can't simply omit Timestamp from comparison because then TestRunStarted has nothing else to compare (which causes an error)
                                                                    .Using<Timestamp>(ctx => 1.Should().Be(1))
                                                                    .WhenTypeIs<Timestamp>()

                                                                    .AllowingInfiniteRecursion()
                                                                    //.RespectingRuntimeTypes()
                                                                    .ExcludingFields()
                                                                    .WithStrictOrdering()
                                                                    );
        }
        private void SetupCrossReferences(IEnumerable<Envelope> messages, Dictionary<Type, HashSet<string>> IDsByType, Dictionary<Type, HashSet<object>> elementsByType, Dictionary<string, HashSet<object>> elementsByID)
        {
            var xrefBuilder = new CrossReferenceBuilder(msg =>
                {
                    InsertIntoElementsByType(msg, elementsByType);

                    if (msg.HasId())
                    {
                        InsertIntoElementsById(msg, elementsByID);
                        InsertIntoIDsByType(msg, IDsByType);
                    }
                });
            foreach (var message in messages)
            {
                var msg = message.Content();
                CucumberMessageVisitor.Accept(xrefBuilder, msg);
            }
        }
        private static void InsertIntoIDsByType(object msg, Dictionary<Type, HashSet<string>> IDsByType)
        {
            if (!IDsByType.ContainsKey(msg.GetType()))
            {
                IDsByType.Add(msg.GetType(), new HashSet<string>());
            }
            IDsByType[msg.GetType()].Add(msg.Id());
        }

        private static void InsertIntoElementsById(object msg, Dictionary<string, HashSet<object>> elementsByID)
        {
            if (!elementsByID.ContainsKey(msg.Id()))
            {
                elementsByID.Add(msg.Id(), new HashSet<object>());
            }
            elementsByID[msg.Id()].Add(msg);
        }

        private static void InsertIntoElementsByType(object msg, Dictionary<Type, HashSet<object>> elementsByType)
        {
            if (!elementsByType.ContainsKey(msg.GetType()))
            {
                elementsByType.Add(msg.GetType(), new HashSet<object>());
            }
            elementsByType[msg.GetType()].Add(msg);
        }

        public void ResultShouldPassAllComparisonTests()
        {
            var method = typeof(CucumberMessagesValidator).GetMethod(nameof(CompareMessageType), BindingFlags.NonPublic | BindingFlags.Instance);
            using (new AssertionScope())
            {
                foreach (Type t in EnvelopeTypes)
                {
                    var genMethod = method!.MakeGenericMethod(t);
                    genMethod.Invoke(this, null);
                }
            }
        }


        private void TestCasesShouldBeComparable()
        {
            CompareMessageType<TestCase>();
        }

        private void StepDefinitionsShouldBeComparable()
        {
            CompareMessageType<StepDefinition>();
        }

        private void PicklesShouldBeComparable()
        {
            CompareMessageType<Pickle>();
        }

        private void GherkinDocumentShouldBeComparable()
        {
            CompareMessageType<GherkinDocument>();

        }

        private void CompareMessageType<T>()
        {
            if (!expecteds_elementsByType.ContainsKey(typeof(T)))
                return;

            HashSet<object>? actuals;
            List<T> actual;
            List<T> expected;

            if (actuals_elementsByType.TryGetValue(typeof(T), out actuals))
            {
                actual = actuals.OfType<T>().ToList();
            }
            else
                actual = new List<T>();

            expected = expecteds_elementsByType[typeof(T)].AsEnumerable().OfType<T>().ToList<T>(); ;

            actual.Should().BeEquivalentTo(expected, options => options
                                                                .Using<List<Hook>>(ctx =>
                                                                {
                                                                    if (ctx.SelectedNode.IsRoot)
                                                                    {
                                                                        var actualList = ctx.Subject;
                                                                        var expectedList = ctx.Expectation;

                                                                        if (expectedList == null || !expectedList.Any())
                                                                        {
                                                                            return; // If expected is null or empty, we don't need to check anything
                                                                        }

                                                                        actualList.Should().NotBeNull();
                                                                        actualList.Should().HaveCountGreaterThanOrEqualTo(expectedList.Count,
                                                                            "actual collection should have at least as many items as expected");

                                                                        foreach (var expectedItem in expectedList)
                                                                        {
                                                                            actualList.Should().Contain(actualItem =>
                                                                                AssertionExtensions.Should(actualItem).BeEquivalentTo(expectedItem, "").And.Subject == actualItem,
                                                                                "actual collection should contain an item equivalent to {0}", expectedItem);
                                                                        }
                                                                    }
                                                                })
                                                                .WhenTypeIs<List<Hook>>()
                                                                // Using a custom string comparison that deals with ISO langauge codes when the property name ends with "Language"
                                                                    .Using<string>(ctx =>
                                                                        {
                                                                            var actual = ctx.Subject.Split("-")[0];
                                                                            var expected = ctx.Expectation.Split("-")[0];
                                                                            actual.Should().Be(expected);
                                                                        })
                                                                    .When(inf => inf.Path.EndsWith("Language"))
                                                                    .WithTracing());
        }

        private void SourceContentShouldBeIdentical()
        {
            CompareMessageType<Source>();
        }

        public void ResultShouldPassBasicSanityChecks()
        {
            EachTestStepShouldProperlyReferToAPickleAndStepDefinitionOrHook();
            EachPickleAndPickleStepShouldBeReferencedByTestStepsAtLeastOnce();
        }

        private void EachTestStepShouldProperlyReferToAPickleAndStepDefinitionOrHook()
        {
        }

        private void EachPickleAndPickleStepShouldBeReferencedByTestStepsAtLeastOnce()
        {
        }

        public void ShouldPassBasicStructuralChecks()
        {
            var actual = actualEnvelopes;
            var expected = expectedEnvelopes;
            actual.Count().Should().BeGreaterThanOrEqualTo(expected.Count());

            // This checks that each top level Envelope content type present in the actual is present in the expected in the same number (except for hooks)
            foreach (var messageType in CucumberMessageExtensions.EnvelopeContentTypes)
            {
                if (actuals_elementsByType.ContainsKey(messageType) && !expecteds_elementsByType.ContainsKey(messageType))
                {
                    throw new System.Exception($"{messageType} present in the actual but not in the expected.");
                }
                if (!actuals_elementsByType.ContainsKey(messageType) && expecteds_elementsByType.ContainsKey(messageType))
                {
                    throw new System.Exception($"{messageType} present in the expected but not in the actual.");
                }
                if (messageType != typeof(Hook) && actuals_elementsByType.ContainsKey(messageType))
                {
                    actuals_elementsByType[messageType].Count().Should().Be(expecteds_elementsByType[messageType].Count());
                }
                if (messageType == typeof(Hook) && actuals_elementsByType.ContainsKey(messageType))
                    actuals_elementsByType[messageType].Count().Should().BeGreaterThanOrEqualTo(expecteds_elementsByType[messageType].Count());
            }
        }
    }
}