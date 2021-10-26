using System.Threading.Tasks;
using FluentValidation;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace SampleWebApi.Controllers.Issue96
{
    /// <summary>
    /// It's like <see cref="IHttpContextAccessor"/> but for <see cref="Tenant96"/> that constructs in runtime in controller.
    /// </summary>
    public interface ITenantAccessor
    {
        Tenant96 Tenant { get; set; }
    }

    public class TenantAccessor : ITenantAccessor
    {
        public Tenant96 Tenant { get; set; }
    }

    public interface IPersonRepository
    {
        Task<Person96> GetTenantOwner(string tenantName);
    }

    #region Models

    public class Tenant96
    {
        public Person96 Owner { get; set; }
    }

    public class Person96
    {
        public Account96 Account { get; set; }
    }

    public class Account96
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    public class StrictAccount96 : Account96
    {
    }

    #endregion

    #region Validators

    [UsedImplicitly]
    public class TenantValidator : AbstractValidator<Tenant96>
    {
        // NOTE: all validators injects by DI, see: https://docs.fluentvalidation.net/en/latest/aspnet.html#injecting-child-validators
        public TenantValidator(IValidator<Person96> personValidator)
        {
            RuleFor(x => x.Owner)
                .SetValidator(personValidator);
        }
    }

    [UsedImplicitly]
    public class PersonValidator : AbstractValidator<Person96>
    {
        // NOTE: all validators injects by DI, see: https://docs.fluentvalidation.net/en/latest/aspnet.html#injecting-child-validators
        public PersonValidator(StrictAccountValidator strictAccountValidator)
        {
            RuleFor(x => x.Account)
                .SetInheritanceValidator(x =>
                {
                    x.Add(strictAccountValidator);
                });
        }
    }

    public class StrictAccountValidator : AbstractValidator<Account96>
    {
        // Injects ITenantAccessor that fills in runtime just before controller method call
        public StrictAccountValidator(ITenantAccessor tenantAccessor)
        {
            // Use tenant as you want for validation
            Tenant96 tenant = tenantAccessor.Tenant;

            this.RuleFor(x => x.UserName)
                .NotEmpty()
                .MaximumLength(50);

            this.RuleFor(x => x.Password)
                .MaximumLength(256);
        }
    }

    #endregion

    public static class Issue96ServiceCollectionExtensions
    {
        public static IServiceCollection AddIssue96(this IServiceCollection services)
        {
            // Registers scoped ITenantAccessor to use in per controller call
            services.AddScoped<ITenantAccessor, TenantAccessor>();

            // Register sample person repo
            services.AddSingleton<IPersonRepository, PersonRepository>();
            return services;
        }
    }

    public class PersonRepository : IPersonRepository
    {
        /// <inheritdoc />
        public async Task<Person96> GetTenantOwner(string tenantName)
        {
            // TODO: from real DB.
            return new Person96() { Account = new Account96() { UserName = tenantName } };
        }
    }

    [Route("api/{tenant96}/[controller]")]
    [ApiController]
    public class Issue96 : Controller
    {
        protected ITenantAccessor TenantAccessor { get; private set; }

        /// <inheritdoc />
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // NOTE: Executes before controller method call. Also can be done with [Microsoft.AspNetCore.Mvc.TypeFilter]
            await ConstructTenantFromContext(context.HttpContext);
            await base.OnActionExecutionAsync(context, next);
        }

        protected async Task ConstructTenantFromContext(HttpContext httpContext)
        {
            // Get tenant name from url
            var tenantName = RouteData.Values.TryGetValue("tenant", out object tenantValue) ? (string)tenantValue : "default";

            // Get tenantOwner from db
            var personRepository = httpContext.RequestServices.GetService<IPersonRepository>();
            var tenantOwner = await personRepository.GetTenantOwner(tenantName)!;

            // Initialize ITenantAccessor that can be used anywhere as DI injected service.
            TenantAccessor = httpContext.RequestServices.GetService<ITenantAccessor>();
            TenantAccessor.Tenant = new Tenant96 { Owner = tenantOwner };
        }

        [HttpPost("[action]")]
        public ActionResult<Tenant96> AddTenant96(Tenant96 tenant96)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            return tenant96;
        }
    }
}