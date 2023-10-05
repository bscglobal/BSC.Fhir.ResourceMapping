using System.Text.Json;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;

namespace BSC.Fhir.Mapping;

public static class QuestionnaireItemComponentExtensions
{
    private const string QUESTIONNAIRE_ITEM_CALCULATED_EXPRESSION =
        "http://hl7.org/fhir/uv/sdc/StructureDefinition-sdc-questionnaire-calculatedExpression.html";

    public static QuestionnaireResponse.ItemComponent CreateQuestionnaireResponseItem(
        this Questionnaire.ItemComponent questionnaireItem
    )
    {
        var responseItem = new QuestionnaireResponse.ItemComponent()
        {
            LinkId = questionnaireItem.LinkId,
            Answer = questionnaireItem.CreateQuestionnaireResponseItemAnswers()
        };

        if (questionnaireItem.ShouldHaveNestedItemsUnderAnswers() && responseItem.Answer?.Count > 0)
        {
            responseItem.AddNestedItemsToAnswer(questionnaireItem);
        }
        else if (questionnaireItem.Type == Questionnaire.QuestionnaireItemType.Group)
        {
            foreach (var item in questionnaireItem.Item)
            {
                responseItem.Item.Add(item.CreateQuestionnaireResponseItem());
            }
        }

        return responseItem;
    }

    public static List<QuestionnaireResponse.AnswerComponent>? CreateQuestionnaireResponseItemAnswers(
        this Questionnaire.ItemComponent questionnaireItem
    )
    {
        if (
            questionnaireItem.Initial.Count == 0
            || (questionnaireItem.GetInitialFirstRep().Value is Quantity quantity && quantity.Value is null)
        )
        {
            return null;
        }

        if (
            questionnaireItem.Type == Questionnaire.QuestionnaireItemType.Group
            || questionnaireItem.Type == Questionnaire.QuestionnaireItemType.Display
        )
        {
            throw new ArgumentException(
                $"Questionnaire item {questionnaireItem.LinkId} has initial value(s) and is a group or display item. See rule que-8 at https://www.hl7.org/fhir/questionnaire-definitions.html#Questionnaire.item.initial."
            );
        }

        if (questionnaireItem.Initial.Count > 1 && !(questionnaireItem.Repeats ?? false))
        {
            throw new ArgumentException(
                $"Questionnaire item {questionnaireItem.LinkId} can only have multiple initial values for repeating items. See rule que-13 at https://www.hl7.org/fhir/questionnaire-definitions.html#Questionnaire.item.initial."
            );
        }

        return questionnaireItem.Initial
            .Select(initial => new QuestionnaireResponse.AnswerComponent { Value = initial.Value })
            .ToList();
    }

    public static Questionnaire.InitialComponent GetInitialFirstRep(this Questionnaire.ItemComponent questionnaireItem)
    {
        Questionnaire.InitialComponent t;
        if (questionnaireItem.Initial.Count == 0)
        {
            t = new Questionnaire.InitialComponent();
            questionnaireItem.Initial.Add(t);
        }
        else
        {
            t = questionnaireItem.Initial.First();
        }

        return t;
    }

    public static bool ShouldHaveNestedItemsUnderAnswers(this Questionnaire.ItemComponent questionnaireItem)
    {
        return questionnaireItem.Item.Count > 0
            && (
                questionnaireItem.Type != Questionnaire.QuestionnaireItemType.Group
                || !(questionnaireItem.Repeats ?? false)
            );
    }

    public static void AddNestedItemsToAnswer(
        this QuestionnaireResponse.ItemComponent questionnaireResponseItem,
        Questionnaire.ItemComponent questionnaireItem
    )
    {
        foreach (var answer in questionnaireResponseItem.Answer)
        {
            answer.Item = questionnaireItem.GetNestedQuestionnaireResponseItems().ToList();
        }
    }

    public static IEnumerable<QuestionnaireResponse.ItemComponent> GetNestedQuestionnaireResponseItems(
        this Questionnaire.ItemComponent questionnaireItem
    )
    {
        return questionnaireItem.Item.Select(item => item.CreateQuestionnaireResponseItem());
    }

    public static EvaluationResult? CalculatedExpressionResult(
        this Questionnaire.ItemComponent questionnaireItem,
        MappingContext context
    )
    {
        var extension = questionnaireItem.Extension.FirstOrDefault(
            e => e.Url == QUESTIONNAIRE_ITEM_CALCULATED_EXPRESSION
        );

        if (extension is null || !(extension.Value is Expression expression))
        {
            return null;
        }

        if (expression.Language != "text/fhirpath")
        {
            return null;
        }

        return FhirPathMapping.EvaluateExpr(expression.Expression_, context);
    }
}
