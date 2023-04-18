using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgileObjects.ReadableExpressions;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore.Query;

namespace hc_ef_custom;

public static class ObjectTypeDescriptorExtensions
{
	public static Test1<TDto> Mapped<TDto>(
		this IObjectTypeDescriptor<TDto> descriptor
	) where TDto : BaseDto
		=> new(descriptor);
}

public class Test1<TDto> where TDto : BaseDto
{
	private readonly IObjectTypeDescriptor<TDto> _descriptor;

	public Test1(IObjectTypeDescriptor<TDto> descriptor)
	{
		_descriptor = descriptor;
	}

	public void To<TEntity>(Action<MappingDescriptor<TDto, TEntity>>? configure = null)
	{
		configure?.Invoke(new(_descriptor));

		_descriptor.Name(typeof(TEntity).Name);

		_descriptor.Ignore(d => d._Meta); // NOTE: We do our configuration (such as ignoring the meta property) after the user code, because we want it to take precedence.

		// NOTE:
		Mappings.Types[typeof(TEntity)] = typeof(TDto);
		Mappings.Types[typeof(TDto)] = typeof(TEntity);

		_descriptor.Extend().OnBeforeCompletion((c, d) =>
		{
			foreach (var field in d.Fields) // NOTE: We examine the type's fields right before the configuration is all done so that we operate upon exactly the fields that are going to be part of the type in the schema. The user might have removed (ignored) or added fields before this.
			{
				if (field.IsIntrospectionField)
					continue;

				if (field.Member is null)
					throw new InvalidOperationException("All fields in a mapped type must correspond to a property on the DTO type.");  // NOTE: This prevents the user from creating arbitrary new fields (e.g. `descriptor.Field("FooBar")`).

				var dtoProp = (PropertyInfo)field.Member; // NOTE: We assume the member behind the field is a property (and this assumption in practically safe in our case, although not safe in principle, if you will)

				if (Mappings.PropertyExpressions.ContainsKey(dtoProp))
					continue;

				if (d.HasInterfaces)
				{
					var baseType = dtoProp.ReflectedType!.BaseType; // TODO: Perhaps not the most elegant of algorithms
					var dtoBaseTypeProp = baseType?.GetProperty(dtoProp.Name, dtoProp.PropertyType); // TODO: Should be before the `continue` above
					if (dtoBaseTypeProp is not null)
					{
						if (
							!Mappings.PropertyAuthRules.ContainsKey(dtoProp) && // NOTE: If there are already auth rules for this property, we don't touch them.
							Mappings.PropertyAuthRules.TryGetValue(dtoBaseTypeProp, out var authRules)
						)
						{
							Mappings.PropertyAuthRules[dtoProp] = new(authRules); // NOTE: We create a new list and copy `authRules`'s element into it, as opposed to just assign a reference to the same `authRules` here.
						}

						// NOTE: Try defaulting to the expression on the base type's property, if it indeed exists, and inheriting its auth rules:
						if (Mappings.PropertyExpressions.TryGetValue(dtoBaseTypeProp, out var expr))
						{
							Mappings.PropertyExpressions[dtoProp] = expr;
							continue;
						}
					}
				}

				var namesakeEntityProp = typeof(TEntity).GetProperty(dtoProp.Name); // NOTE: Property on the entity type with the same name.
				if (
					namesakeEntityProp is null ||
					!Helpers.AreAssignable(dtoProp.PropertyType, namesakeEntityProp.PropertyType)
				)
					throw new InvalidOperationException($"Property '{dtoProp.Name}' on the DTO type '{typeof(TDto)}' was not configured explicitly and no implicitly matching property with the same name and type on the entity type was found..");

				// NOTE: Doing this here as opposed to in the projection middleware has two advantages: 1. No reflection at runtime (only on startup) 2. If no matching entity property exists we throw on startup instead of at runtime.
				var param = Expression.Parameter(typeof(TEntity));
				var body = Expression.Property(param, namesakeEntityProp);
				var expression = Expression.Lambda(body, param);
				Mappings.PropertyExpressions[dtoProp] = expression;
			}
		});
	}
}

public class MappingDescriptor<TDto, TEntity>
{
	private readonly IObjectTypeDescriptor<TDto> _descriptor;

	public MappingDescriptor(IObjectTypeDescriptor<TDto> descriptor)
	{
		_descriptor = descriptor;
	}

	public PropertyMappingDescriptor<TDto, TEntity, TProperty> Property<TProperty>(
		Expression<Func<TDto, TProperty?>> propertySelector
	)
		=> new(_descriptor.Field(propertySelector));
}

public class PropertyMappingDescriptor<TDto, TEntity, TProperty>
{
	private readonly IObjectFieldDescriptor _descriptor;

	public PropertyMappingDescriptor(IObjectFieldDescriptor descriptor)
	{
		_descriptor = descriptor;
	}

	// NOTE: We don't enforce that `TResult` is the same as the property's type because it could be an entity that's mappable to a DTO (e.g. )
	public PropertyMappingDescriptor<TDto, TEntity, TProperty> MapTo<TResult>(
		Expression<Func<TEntity, TResult>> expression
	)
	{
		_descriptor.Extend().OnBeforeCreate(d =>
		{
			var property = (PropertyInfo)d.Member!;
			if (!Helpers.AreAssignable(property.PropertyType, typeof(TResult)))
				throw new InvalidOperationException($"The type '{typeof(TResult)}' of the provided expression '{expression}' is not assignable to the property '{property}'.");
			Mappings.PropertyExpressions[property] = expression;
		});
		return this;
	}

	public PropertyMappingDescriptor<TDto, TEntity, TProperty> UseAuth(
		Action<PropertyAuthMappingDescriptor<TDto, TEntity, TProperty>> configure
	)
	{
		configure(new(_descriptor));
		return this;
	}

	public PropertyMappingDescriptor<TDto, TEntity, TProperty> Format(
		Func<TProperty, TProperty> transformer
	)
	{
		_descriptor.Use(next => async context =>
		{
			await next(context);
			if (context.Result is not null)
				context.Result = transformer((TProperty)context.Result);
		});
		return this;
	}
}

public class PropertyAuthMappingDescriptor<TDto, TEntity, TProperty>
{
	// TODO: Use C# 12's primary constructors
	private readonly IObjectFieldDescriptor _descriptor;

	public PropertyAuthMappingDescriptor(IObjectFieldDescriptor descriptor)
	{
		_descriptor = descriptor;
	}

	// TODO: Use C# 12's type aliases for the return types of these methods
	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustBeAuthenticated(
		Func<IResolverContext, ISelection, bool>? shouldApply = null
	)
		=> MustPre(currentUser => currentUser is not null, shouldApply);

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustNotBeAuthenticated() =>
 		MustPre(currentUser => currentUser is null);

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustHaveRole(UserRole role) =>
		MustPre(currentUser => currentUser!.Role == role);

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustNotHaveRule(UserRole role) =>
		MustPre(currentUser => currentUser!.Role != role);

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustPre(
		Func<AuthenticatedUser?, bool> isPermitted,
		Func<IResolverContext, ISelection, bool>? shouldApply = null
	)
	{
		string key = Guid.NewGuid().ToString();

		_descriptor.Extend().OnBeforeCreate(d =>
		{
			Mappings.PropertyAuthRules.AddValueItem(
				(PropertyInfo)d.Member!,
				new PreAuthRule
				{
					Key = key,
					IsPermitted = isPermitted,
					ShouldApply = shouldApply,
				}
			);
		});

		_descriptor.Use(next => async context =>
		{
			await next(context);

			bool? permitted = context.GetScopedStateOrDefault<bool?>(key); // NOTE: The item could not exist in the scoped context (and by extension the returned value here could be `null`), for example when the `ShouldApply` returns false or a prior pre auth rule fails (pre auth rules have a "fail-fast" kind of behavior.)
			if (permitted == false) // NOTE: If `permitted` is `null`, we assume permission.
				context.ReportAuthError();
		});

		return this;
	}

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> Must(
		Expression<Func<AuthenticatedUser?, TEntity, bool>> expression,
		Func<IResolverContext, ISelection, bool>? shouldApply = null
	)
	{
		string key = ExpressionEqualityComparer.Instance.GetHashCode(expression).ToString(); // TODO: This is almost perfect, the only problem currently is that the hash will be different if the parameters names are different, which is strange. See https://github.com/dotnet/efcore/issues/30697

		_descriptor.Extend().OnBeforeCreate(d =>
		{
			Mappings.PropertyAuthRules.AddValueItem(
				(PropertyInfo)d.Member!,
				new MetaAuthRule
				{
					Key = key,
					Expression = expression,
					ShouldApply = shouldApply,
				}
			);
		});

		_descriptor.Use(next => async context =>
		{
			await next(context);

			BaseDto parent = context.Parent<BaseDto>();
			bool? permitted = parent._Meta?.GetValueOrDefault(key, true); // NOTE: In cases where the `ShouldApply` returns `false`, the `_Meta` property here will either be null, or will not contain this particular rule's key.
			if (permitted == false)
				context.ReportAuthError();
		});

		return this;
	}

	/// <summary>
	/// Adds an applicability condition to all the previously added auth rules that enforces that the
	/// given property must be selected on the child property (which must be an complex type itself)
	/// in order for the rule to apply.
	/// </summary>
	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> WhenSelected<TInnerProperty>(
		Expression<Func<TProperty, TInnerProperty>> innerPropertySelector
	)
	{
		// TODO: Would've been nice if there was a way to avoid all of this manual logic:
		if (
			innerPropertySelector.Body is not MemberExpression memberExpr ||
			memberExpr.Member is not PropertyInfo innerProp ||
			innerProp.DeclaringType != typeof(TProperty) // NOTE: The user shouldn't choose a "deep" property
		)
			throw new InvalidOperationException();

		_descriptor.Extend().OnBeforeCreate(d =>
		{
			if (Mappings.PropertyAuthRules.TryGetValue((PropertyInfo)d.Member!, out var rules))
			{
				foreach (var rule in rules)
				{
					// TODO: A cache for the result of these functions?
					rule.ShouldApply ??= (context, selection) =>
					{
						var type = context.Operation.GetPossibleTypes(selection).Single(); // TODO: Good enough for now, but
						var childSelections = context.GetSelections(type, selection);
						return childSelections.Any(s => s.Field.Member == innerProp);
					};
				}
			}
		});
		return this;
	}
}

public static class MiddlewareContextExtensions
{
	public static void ReportAuthError(this IMiddlewareContext context)
	{
		var error = ErrorBuilder.New()
			.SetMessage("شما اجازه دسترسی به این فیلد را ندارید.")
			.SetCode("NOT_AUTHORIZED")
			.SetPath(context.Path)
			.AddLocation(context.Selection.SyntaxNode)
			.Build();
		context.ReportError(error);
		context.Result = null;
	}
}

// TODO: Use C# 12's type alias for `Func<IResolverContext, ISelection, bool>` and so on.
public abstract class AuthRule
{
	public required string Key { get; set; }
	public required Func<IResolverContext, ISelection, bool>? ShouldApply { get; set; }
}
public class PreAuthRule : AuthRule
{
	public required Func<AuthenticatedUser?, bool> IsPermitted { get; set; }
}
public class MetaAuthRule : AuthRule
{
	public required LambdaExpression Expression { get; set; } // NOTE: This is actually an `Expression<Func<AuthenticatedUser?, object, bool>>` but we can't use that type because it won't work.
}

public sealed class UseProjectorAttribute : ObjectFieldDescriptorAttribute
{
	private ResultType _resultType;
	public UseProjectorAttribute(ResultType resultType, [CallerLineNumber] int order = 0)
	{
		_resultType = resultType;
		Order = order;
	}

	protected override void OnConfigure(
		IDescriptorContext context,
		IObjectFieldDescriptor descriptor,
		MemberInfo member
	)
	{
		descriptor.Use((_, next) => new CustomProjectionMiddleware(next, _resultType));

		descriptor.Extend().OnBeforeCreate((c, d) =>
		{
			if (d.Type is not ExtendedTypeReference typeRef ||
				d.ResultType is null ||
				!d.ResultType.IsAssignableTo(typeof(IQueryable<object>)))
				throw new InvalidOperationException($"The projector middleware cannot be used on field with result type of '{d.ResultType}'.");

			// NOTE: In part inspired by https://github.com/ChilliCream/graphql-platform/blob/main/src/HotChocolate/Data/src/Data/Projections/Extensions/SingleOrDefaultObjectFieldDescriptorExtensions.cs
			var entityType = c.TypeInspector.CreateTypeInfo(typeRef.Type).NamedType; // TODO: I don't know why `c.TypeInspector.ExtractNamedType` doesn't work here
			var correspondingDtoType = Mappings.Types[entityType];
			d.Type = TypeReference.Create(_resultType switch
			{
				ResultType.Single => c.TypeInspector.GetType(correspondingDtoType), // NOTE: Similar to the behavior of Hot Chocolate's own `UseSingleOrDefault` middleware, which always makes the resulting singular type nullable, regardless of the original type's nullability, hence the "OrDefault" part. This is because the set (that the IQueryable represents) might be empty, in which case it has to return null for the field.
				ResultType.Multiple => c.TypeInspector.GetType(
					typeof(IEnumerable<>).MakeGenericType(correspondingDtoType),
					c.TypeInspector.CollectNullability(typeRef.Type) // NOTE: Preserve the nullability state of the original type
				),
				_ => throw new ArgumentOutOfRangeException(),
			});
		});
	}
}

public enum ResultType
{
	Single,
	Multiple
}

public static class Mappings // TODO: This should ideally be part of some (Hot Chocolate) schema-wide context data
{
	// TODO: Would be nice if the versions of these that the middleware accesses were read-only dictionaries
	public static Dictionary<Type, Type> Types = new();
	public static Dictionary<PropertyInfo, LambdaExpression> PropertyExpressions = new();
	public static Dictionary<PropertyInfo, List<AuthRule>> PropertyAuthRules = new();
}

public static class DictionaryExtensions
{
	public static void AddValueItem<TKey, TValueItem>(
		this Dictionary<TKey, List<TValueItem>> dict,
		TKey key,
		TValueItem newValueItem
	) where TKey : notnull
	{
		if (dict.TryGetValue(key, out var existingList))
			existingList.Add(newValueItem);
		else
			dict.Add(key, new() { newValueItem });
	}
}

public enum UserRole
{
	Admin
}

public record AuthenticatedUser(
	int Id,
	UserRole? Role
);

public static class Helpers // TODO
{
	public static bool AreAssignable(Type dtoProp, Type entityProp)
	{
		if (dtoProp.IsAssignableFrom(entityProp)) // NOTE: Simple cases like where the types are directly assignable
			return true;

		// TODO: Improve
		if (
			entityProp.IsAssignableTo(typeof(IEnumerable<object>)) &&
			dtoProp.IsAssignableTo(typeof(IEnumerable<object>))
		)
		{
			entityProp = entityProp.GetGenericArguments().Single();
			dtoProp = dtoProp.GetGenericArguments().Single();
		}
		var entityPropDtoType = Mappings.Types.GetValueOrDefault(entityProp);
		if (entityPropDtoType is not null && dtoProp.IsAssignableFrom(entityPropDtoType))
			return true;

		return false;
	}
}

public class AuthRetriever
{
	private readonly Lazy<Task<AuthenticatedUser?>> _taskLazy;

	public AuthRetriever(AppDbContext db)
	{
		// NOTE: Lazy<T> is thread-safe by default (so we don't need to explicitly pass "isThreadSafe: true" to the constructor, which is what I initially thought should be done in order to make it thread-safe), and we do need this thread-safety here since GraphQL query fields can run in parallel and therefore the `GetAsync` method below could be called simultaneously by multiple threads. We need to make sure that initialization function (which makes calls to the database) gets executed only once.
		_taskLazy = new(() => db.Lessons.Where(l => l.Id == 1)
			.Select(l => new AuthenticatedUser(l.Id, null))
			.SingleOrDefaultAsync());
	}

	public Task<AuthenticatedUser?> User => _taskLazy.Value;
}
