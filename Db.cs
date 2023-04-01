using EntityFrameworkCore.Projectables;

namespace hc_ef_custom;

public class AppDbContext : DbContext
{
	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
	{
	}

	public DbSet<Author> Authors => Set<Author>();
	public DbSet<Book> Books => Set<Book>();
	public DbSet<BookRating> BookRatings => Set<BookRating>();
}

public class Author
{
	public required int Id { get; set; }
	public required string FirstName { get; set; }
	public required string LastName { get; set; }

	[Projectable]
	public string FullName => FirstName + " " + LastName;

	// Navigation Props:
	public ICollection<Book> Books { get; set; } = default!;
}

public class Book
{
	public required int Id { get; set; }
	public int AuthorId { get; set; }
	public required string Title { get; set; }

	[Projectable]
	public double AverageRating => Ratings.Average(r => r.Rating);

	[Projectable]
	public string Foo(int num) => $"{Title}-${num}";

	// Navigation Props:
	public Author Author { get; set; } = default!;
	public ICollection<BookRating> Ratings { get; set; } = default!;
}

public class BookRating
{
	public required int Id { get; set; }
	public required int BookId { get; set; }
	public required byte Rating { get; set; }

	// Navigation Props:
	public Book Book { get; set; } = default!;
}
