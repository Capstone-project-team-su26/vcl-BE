using System;
using System.Collections.Generic;

namespace VCL_DLL.Models
{
    public partial class Conversation
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public Guid? SalesId { get; set; }
        public string? RelatedType { get; set; }
        public Guid? RelatedId { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual Customer Customer { get; set; }
        public virtual User Sales { get; set; }
        public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
