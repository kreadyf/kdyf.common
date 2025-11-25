namespace kdyf.Operations.Extensions.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public class OperationDescriptorAttribute : Attribute
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public OperationDescriptorAttribute(string name)
        {
            Name = name;
        }

        public OperationDescriptorAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}
