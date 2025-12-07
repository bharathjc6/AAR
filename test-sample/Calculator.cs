namespace SampleApp;

public class Calculator
{
    public int Add(int a, int b) => a + b;
    
    public int Divide(int a, int b)
    {
        // BUG: No division by zero check
        return a / b;
    }
    
    public string GetPassword()
    {
        // SECURITY: Hardcoded password
        return "admin123";
    }
}
