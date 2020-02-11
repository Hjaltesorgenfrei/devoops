using System;

namespace MiniTwit.Web.App.Models
{
    public class Message
    {
        public int Id { get; set;}
        public int AuthorId { get; set;}
        public User Author { get; set; }
        public string Text { get; set;} 
        public int PubDate { get; set;}

        public int Flagged { get; set;}
        
    }
}