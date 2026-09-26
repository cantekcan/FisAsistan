using FisAsistan.Application.Receipts.Dtos;
using FluentValidation;

namespace FisAsistan.Api.Validators;

public class UpdateReceiptFieldsRequestValidator : AbstractValidator<UpdateReceiptFieldsRequest>
{
    public UpdateReceiptFieldsRequestValidator()
    {
        RuleForEach(x => x.Fields).ChildRules(field =>
        {
            field.RuleFor(f => f.FieldName).IsInEnum();
        });

        RuleForEach(x => x.VatLines).ChildRules(vat =>
        {
            vat.RuleFor(v => v.RatePercent).InclusiveBetween(0, 100);
            vat.RuleFor(v => v.VatAmount).GreaterThanOrEqualTo(0);
        });
    }
}

public class RejectReceiptRequestValidator : AbstractValidator<RejectReceiptRequest>
{
    public RejectReceiptRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Reddetme sebebi belirtilmelidir.").MaximumLength(1000);
    }
}
