using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using TransportationService.Api.Common;

namespace TransportationService.Api.Tests.Common;

public class DomainValidationExceptionFilterTests
{
    private static ExceptionContext BuildContext(Exception exception)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, []) { Exception = exception };
    }

    [Fact]
    public void FormWideError_ProducesProblemDetails_WithoutErrorsDictionary()
    {
        var context = BuildContext(new DomainValidationException("Er ging iets mis met de invoer."));
        new DomainValidationExceptionFilter().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Validatiefout", problem.Title);
        Assert.Equal("Er ging iets mis met de invoer.", problem.Detail);
        Assert.False(problem.Extensions.ContainsKey("errors"));
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void FieldError_IsEmittedInErrorsDictionary()
    {
        var context = BuildContext(new DomainValidationException("vatNumber", "Ongeldig BTW-nummer."));
        new DomainValidationExceptionFilter().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("Ongeldig BTW-nummer.", problem.Detail);
        var errors = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(problem.Extensions["errors"]);
        Assert.Equal("Ongeldig BTW-nummer.", Assert.Single(errors["vatNumber"]));
    }

    [Fact]
    public void MultiFieldError_KeepsAllFields()
    {
        var fieldErrors = new Dictionary<string, string[]>
        {
            ["stops[0].city"] = ["Gemeente is verplicht."],
            ["stops[1].city"] = ["Gemeente is verplicht."],
        };
        var context = BuildContext(new DomainValidationException("Controleer de stops.", fieldErrors));
        new DomainValidationExceptionFilter().OnException(context);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(context.Result).Value);
        var errors = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(problem.Extensions["errors"]);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void OtherExceptions_AreIgnored()
    {
        var context = BuildContext(new InvalidOperationException("boom"));
        new DomainValidationExceptionFilter().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
