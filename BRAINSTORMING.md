In our projections, we don't want to be limited to

## - The "DTO alongside a dictionary" approach

Conclusion: Ruled out, because it causes EF to generate two identical left joins in cases where a collection projection is involved.

Example:

```csharp
public class ProjectionResult<T>
{
	public T Main { get; set; } = default!;
	public Dictionary<string, object> Auth { get; set; } = default!;
}
```

```csharp
var result = await db.Books
	.Where(b => b.Id == id)
	.Select(b => new ProjectionResult<BookDto>
	{
		Main = new()
		{
			Title = b.Title,
			Ratings = b.Ratings.Select(r => new BookRatingDto
			{
				Rating = r.Rating,
			}),
		},
		Auth = new()
		{
			{ "Title_StartsWithCrap",  b.Title.StartsWith("Crap") },
			{ "Ratings", b.Ratings.Select(r => new Dictionary<string, object> {
				{ "Rating_IsGreaterThan3", r.Rating > 3 }
			}) }
		},
	})
	.FirstOrDefaultAsync();
```

Generates this SQL:

```sql
SELECT t."Title", t."Id", b0."Rating", b0."Id", t.c, b1."Rating" > 3, b1."Id"
FROM (
	SELECT b."Title", b."Title" LIKE 'Crap%' AS c, b."Id"
	FROM "Books" AS b
	WHERE b."Id" = @__id_0
	LIMIT 1
) AS t
LEFT JOIN "BookRatings" AS b0 ON t."Id" = b0."BookId"
LEFT JOIN "BookRatings" AS b1 ON t."Id" = b1."BookId"
ORDER BY t."Id", b0."Id"
```

Along with this warning from EF:

```
Compiling a query which loads related collections for more than one collection navigation, either via 'Include' or through projection, but no 'QuerySplittingBehavior' has been configured. By default, Entity Framework will use 'QuerySplittingBehavior.SingleQuery', which can potentially result in slow query performance. See https://go.microsoft.com/fwlink/?linkid=2134277 for more information. To identify the query that's triggering this warning call 'ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))'.
```

## - The array approach:

At the same time as building the projection expression, we need to also build a separate expression (which will then compile into a delegate and call) that takes that array, and converts it into the DTO type. It would be lambda expression, whose body is a member initialization expression.

Example:

```csharp
book => new[]
{
    book.Title,
    book.Author.FirstName
}
```

```csharp
resultArr => new BookDto
{
	Title = (string)resultArr[0],
	Author = new AuthorDto
	{
		FirstName = (string)resultArr[1],
	},
};
```

## - The (only) dictionary approach:

## - The runtime derived type creation approach

Other:

- The anonymous type approach (yields no benefit over dictionary and runtime derived type approaches)
