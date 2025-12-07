namespace SampleApp;

public interface IRepository<T>
{
    T GetById(int id);
    void Save(T entity);
    void Delete(int id);
}

public class UserRepository : IRepository<User>
{
    private readonly string _connectionString = "Server=prod;Password=secret123";
    
    public User GetById(int id)
    {
        // TODO: Implement actual database call
        throw new NotImplementedException();
    }
    
    public void Save(User entity)
    {
        // SQL Injection vulnerability
        var sql = $"INSERT INTO Users VALUES ('{entity.Name}')";
    }
    
    public void Delete(int id) { }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
}
