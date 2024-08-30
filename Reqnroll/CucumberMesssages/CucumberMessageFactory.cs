﻿using Cucumber.Messages;
using Gherkin.CucumberMessages;
using Io.Cucumber.Messages.Types;
using Reqnroll.Bindings;
using Reqnroll.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Reqnroll.CucumberMesssages
{
    internal class CucumberMessageFactory
    {
        public static TestRunStarted ToTestRunStarted(FeatureEventProcessor featureState, FeatureStartedEvent featureStartedEvent)
        {
            return new TestRunStarted(Converters.ToTimestamp(featureStartedEvent.Timestamp));
        }

        public static TestRunFinished ToTestRunFinished(FeatureEventProcessor featureState, FeatureFinishedEvent featureFinishedEvent)
        {
            return new TestRunFinished(null, featureState.Success, Converters.ToTimestamp(featureFinishedEvent.Timestamp), null);
        }
        internal static TestCase ToTestCase(ScenarioEventProcessor scenarioState, ScenarioStartedEvent scenarioStartedEvent)
        {
            var testSteps = new List<TestStep>();

            foreach (var stepState in scenarioState.Steps)
            {
                switch (stepState)
                {
                    case ScenarioStepProcessor _:
                        var testStep = CucumberMessageFactory.ToTestStep(scenarioState, stepState as ScenarioStepProcessor);
                        testSteps.Add(testStep);
                        break;
                    case HookStepProcessor _:
                        var hookTestStep = CucumberMessageFactory.ToHookTestStep(stepState as HookStepProcessor);
                        testSteps.Add(hookTestStep);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            var testCase = new TestCase
            (
                scenarioState.TestCaseID,
                scenarioState.PickleID,
                testSteps
            );
            return testCase;
        }
        internal static TestCaseStarted ToTestCaseStarted(ScenarioEventProcessor scenarioState, ScenarioStartedEvent scenarioStartedEvent)
        {
            return new TestCaseStarted(0, scenarioState.TestCaseStartedID, scenarioState.TestCaseID, null, Converters.ToTimestamp(scenarioStartedEvent.Timestamp));
        }
        internal static TestCaseFinished ToTestCaseFinished(ScenarioEventProcessor scenarioState, ScenarioFinishedEvent scenarioFinishedEvent)
        {
            return new TestCaseFinished(scenarioState.TestCaseStartedID, Converters.ToTimestamp(scenarioFinishedEvent.Timestamp), false);
        }
        internal static StepDefinition ToStepDefinition(IStepDefinitionBinding binding, IIdGenerator idGenerator)
        {
            var bindingSourceText = binding.SourceExpression;
            var expressionType = binding.ExpressionType;
            var stepDefinitionPatternType = expressionType switch { StepDefinitionExpressionTypes.CucumberExpression => StepDefinitionPatternType.CUCUMBER_EXPRESSION, _ => StepDefinitionPatternType.REGULAR_EXPRESSION };
            var stepDefinitionPattern = new StepDefinitionPattern(bindingSourceText, stepDefinitionPatternType);
            SourceReference sourceRef = ToSourceRef(binding);

            var result = new StepDefinition
            (
                idGenerator.GetNewId(),
                stepDefinitionPattern,
                sourceRef
            );
            return result;
        }


        internal static ParameterType ToParameterType(IStepArgumentTransformationBinding stepTransform, IIdGenerator iDGenerator)
        {
            var regex = stepTransform.Regex.ToString();
            var name = stepTransform.Name;
            var result = new ParameterType
            (
                name,
                new List<string>
                {
                    regex
                },
                false,
                false,
                iDGenerator.GetNewId(),
                ToSourceRef(stepTransform)
            );
            return result;
        }

        private static SourceReference ToSourceRef(IBinding binding)
        {
            var methodName = binding.Method.Name;
            var className = binding.Method.Type.Name;
            var paramTypes = binding.Method.Parameters.Select(x => x.Type.Name).ToList();
            var methodDescription = new JavaMethod(className, methodName, paramTypes);
            var sourceRef = SourceReference.Create(methodDescription);
            return sourceRef;
        }

        internal static TestStep ToTestStep(ScenarioEventProcessor scenarioState, ScenarioStepProcessor stepState)
        {
            bool bound = stepState.StepDefinitionId != null;

            var args = stepState.StepArguments
               .Select(arg => CucumberMessageFactory.ToStepMatchArgument(arg))
               .ToList();

            var result = new TestStep(
                 null,
                 stepState.TestStepID,
                 stepState.PickleStepID,
                 bound ? new List<string> { stepState.StepDefinitionId } : new List<string>(),
                 bound ? new List<StepMatchArgumentsList> { new StepMatchArgumentsList(args) } : new List<StepMatchArgumentsList>()
                 );

            return result;
        }

        internal static StepMatchArgument ToStepMatchArgument(StepArgument argument)
        {
            return new StepMatchArgument(
                new Group(
                    new List<Group>(),
                    null,
                    argument.Value
                    ),
                argument.Type);
        }
        internal static TestStepStarted ToTestStepStarted(ScenarioStepProcessor stepState, StepStartedEvent stepStartedEvent)
        {
            return new TestStepStarted(
                stepState.TestCaseStartedID,
                stepState.TestStepID,
                Converters.ToTimestamp(stepStartedEvent.Timestamp));
        }

        internal static TestStepFinished ToTestStepFinished(ScenarioStepProcessor stepState, StepFinishedEvent stepFinishedEvent)
        {
            return new TestStepFinished(
                stepState.TestCaseStartedID,
                stepState.TestStepID,
                ToTestStepResult(stepState),
                Converters.ToTimestamp(stepFinishedEvent.Timestamp));
        }

        internal static Hook ToHook(IHookBinding hookBinding, IIdGenerator iDGenerator)
        {
            SourceReference sourceRef = ToSourceRef(hookBinding);

            var result = new Hook
            (
                iDGenerator.GetNewId(),
                null,
                sourceRef,
                hookBinding.IsScoped ? hookBinding.BindingScope.Tag : null
            );
            return result;
        }

        internal static TestStep ToHookTestStep(HookStepProcessor hookStepState)
        {
            // find the Hook message at the Feature level
            var hookCacheKey = CanonicalizeHookBinding(hookStepState.HookBindingFinishedEvent.HookBinding);
            var hookId = hookStepState.parentScenario.FeatureState.HookDefinitionsByPattern[hookCacheKey];

            return new TestStep(
                hookId,
                hookStepState.TestStepID,
                null,
                new List<string>(),
                new List<StepMatchArgumentsList>());
        }
        internal static TestStepStarted ToTestStepStarted(HookStepProcessor hookStepProcessor, HookBindingStartedEvent hookBindingStartedEvent)
        {
            return new TestStepStarted(hookStepProcessor.TestCaseStartedID, hookStepProcessor.TestStepID, Converters.ToTimestamp(hookBindingStartedEvent.Timestamp));
        }

        internal static TestStepFinished ToTestStepFinished(HookStepProcessor hookStepProcessor, HookBindingFinishedEvent hookBindingFinishedEvent)
        {
            return new TestStepFinished(hookStepProcessor.TestCaseStartedID, hookStepProcessor.TestStepID, ToTestStepResult(hookStepProcessor), Converters.ToTimestamp(hookBindingFinishedEvent.Timestamp));
        }

        internal static Attachment ToAttachment(ScenarioEventProcessor scenarioEventProcessor, AttachmentAddedEventWrapper attachmentAddedEventWrapper)
        {
            return new Attachment(
                Base64EncodeFile(attachmentAddedEventWrapper.AttachmentAddedEvent.FilePath),
                AttachmentContentEncoding.BASE64,
                Path.GetFileName(attachmentAddedEventWrapper.AttachmentAddedEvent.FilePath),
                FileExtensionToMIMETypeMap.GetMimeType(Path.GetExtension(attachmentAddedEventWrapper.AttachmentAddedEvent.FilePath)),
                null,
                attachmentAddedEventWrapper.TestCaseStartedID,
                attachmentAddedEventWrapper.TestCaseStepID,
                null);
        }
        internal static Attachment ToAttachment(ScenarioEventProcessor scenarioEventProcessor, OutputAddedEventWrapper outputAddedEventWrapper)
        {
            return new Attachment(
                outputAddedEventWrapper.OutputAddedEvent.Text,
                AttachmentContentEncoding.IDENTITY,
                null,
                "text/x.cucumber.log+plain",
                null,
                outputAddedEventWrapper.TestCaseStartedID,
                outputAddedEventWrapper.TestCaseStepID,
                null);
        }

        private static TestStepResult ToTestStepResult(StepProcessorBase stepState)
        {
            return new TestStepResult(
                Converters.ToDuration(stepState.Duration),
                "",
                ToTestStepResultStatus(stepState.Status),
                null);

        }

        private static TestStepResultStatus ToTestStepResultStatus(ScenarioExecutionStatus status)
        {
            return status switch
            {
                ScenarioExecutionStatus.OK => TestStepResultStatus.PASSED,
                ScenarioExecutionStatus.BindingError => TestStepResultStatus.AMBIGUOUS,
                ScenarioExecutionStatus.TestError => TestStepResultStatus.FAILED,
                ScenarioExecutionStatus.Skipped => TestStepResultStatus.SKIPPED,
                ScenarioExecutionStatus.UndefinedStep => TestStepResultStatus.UNDEFINED,
                ScenarioExecutionStatus.StepDefinitionPending => TestStepResultStatus.PENDING,
                _ => TestStepResultStatus.UNKNOWN
            };
        }


        #region utility methods
        public static string CanonicalizeStepDefinitionPattern(IStepDefinitionBinding stepDefinition)
        {
            string signature = GenerateSignature(stepDefinition);

            return $"{stepDefinition.SourceExpression}({signature})";
        }

        public static string CanonicalizeHookBinding(IHookBinding hookBinding)
        {
            string signature = GenerateSignature(hookBinding);
            return $"{hookBinding.Method.Type.Name}.{hookBinding.Method.Name}({signature})";
        }

        private static string GenerateSignature(IBinding stepDefinition)
        {
            return stepDefinition.Method != null ? String.Join(",", stepDefinition.Method.Parameters.Select(p => p.Type.Name)) : "";
        }
        public static string Base64EncodeFile(string filePath)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            return Convert.ToBase64String(fileBytes);
        }
        #endregion
    }
}