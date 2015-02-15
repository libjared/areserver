using System;

namespace Areserver
{
    public class Actor
    {
        public int X { get; set; }

        public int Y { get; set; }

        public string Name { get; set; }

        public int Life { get; set; }

        public Actor()
        {
            X = int.MinValue;
            Y = int.MinValue;
            Name = "Cactus Fantastico";
            Life = 100;
        }
    }

    public class Player : Actor
    {
        public long UID { get; set; }

        public Player()
            : base()
        {
            UID = -1;
        }
    }
}

