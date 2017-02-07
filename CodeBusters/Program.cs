using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

/**
 * Send your busters out into the fog to trap ghosts and bring them home!
 **/
class Player
{
    static void Main(string[] args)
    {
        int bustersPerPlayer = int.Parse(Console.ReadLine()); // the amount of busters you control
        int ghostCount = int.Parse(Console.ReadLine()); // the amount of ghosts on the map
        int myTeamId = int.Parse(Console.ReadLine()); // if this is 0, your base is on the top left of the map, if it is one, on the bottom right

        BusterManager manager = new BusterManager(bustersPerPlayer, ghostCount, myTeamId);

        // game loop
        while (true)
        {
            List<GameInformation> gameInformation = new List<GameInformation>();
            int entities = int.Parse(Console.ReadLine()); // the number of busters and ghosts visible to you
            for (int i = 0; i < entities; i++)
            {
                string[] inputs = Console.ReadLine().Split(' ');
                gameInformation.Add(new GameInformation(
                    int.Parse(inputs[0]),
                    int.Parse(inputs[1]),
                    int.Parse(inputs[2]),
                    int.Parse(inputs[3]),
                    int.Parse(inputs[4]),
                    int.Parse(inputs[5]),
                    manager.TeamId));
            }

            manager.SenseEnvironment(gameInformation);
            manager.AssignTargets();
            manager.OrderActions();
        }
    }
}

public class GameInformation
{
    public int EntityId { get; }
    public Coordinate Position { get; }
    public EntityType EntityType { get; } // the team id if it is a buster, -1 if it is a ghost.
    public int State { get; } // For busters: 0=idle, 1=carrying a ghost.
    public int Value { get; } // For busters: Ghost id being carried. For ghosts: number of busters attempting to trap this ghost.

    public GameInformation(int entityId, int x, int y, int entityType, int state, int value, int teamId)
    {
        EntityId = entityId;
        Position = new Coordinate(x, y);
        if (entityType == -1)
        {
            EntityType = EntityType.Ghost;
        }
        else if (entityType == teamId)
        {
            EntityType = EntityType.TeamMate;
        }
        else
        {
            EntityType = EntityType.Opponent;
        }
        State = state;
        Value = value;
    }
}

public class BusterManager
{
    public List<Buster> Busters { get; set; }
    public List<Opponent> Opponents { get; set; }
    public Coordinate Base { get; set; }
    public Strategy Strategy { get; set; }
    public List<Ghost> Ghosts { get; set; }
    public int TeamId { get; set; }

    public BusterManager(int busterPerPlayers, int ghostCount, int teamId)
    {
        TeamId = teamId;
        Ghosts = new List<Ghost>();
        Busters = new List<Buster>();
        Opponents = new List<Opponent>();
        Strategy = Strategy.Default;
        if (teamId == 1)
        {
            Base = new Coordinate(16000,9000);
        }
        else
        {
            Base = new Coordinate();
        }

        for (int i = 0; i < busterPerPlayers; i++)
        {
            if (teamId == 1)
            {
                Busters.Add(new Buster(this, i + busterPerPlayers));
                Opponents.Add(new Opponent(i));
            }
            else
            {
                Busters.Add(new Buster(this, i));
                Opponents.Add(new Opponent(i + busterPerPlayers));
            }          
        }

        for (int i = 0; i < ghostCount; i++)
        {
            Ghosts.Add(new Ghost(i));
        }
    }
    
    public void SenseEnvironment(List<GameInformation> inputs)
    {
        Ghosts.ForEach(f => f.Hidden = true);
        Opponents.ForEach(f => f.Hidden = true);

        foreach (var input in inputs)
        {
            if (input.EntityType == EntityType.TeamMate)
            {
                Buster buster = Busters.Find(f => f.Id == input.EntityId);
                buster.Position = input.Position;
                if (buster.TrackedGhost != null)
                {
                    buster.TrackedGhost.Trapped = input.Value == buster.TrackedGhost.Id ? true : false;
                }             
            }
            else if(input.EntityType == EntityType.Opponent)
            {
                Opponent opponent = Opponents.Find(f => f.Id == input.EntityId);
                opponent.Position = input.Position;
                opponent.Hidden = false;
            }
            else if(input.EntityType == EntityType.Ghost)
            {
                Ghost ghost = Ghosts[input.EntityId];
                ghost.Position = input.Position;
                ghost.Hidden = false;
                ghost.BusterNumber = input.Value;
            }
        }
    }

    public void OrderActions()
    {
        Busters.ForEach(f => Console.WriteLine(f.NextAction()));
    }

    public void AssignTargets()
    {
        List<Buster> BusyBusters = Busters.FindAll(f => f.TrackedGhost != null);

        foreach (var buster in BusyBusters)
        {
            if (buster.TrackedGhost.Dead)
            {
                buster.TrackedGhost = null;
            }
        }

        List<Buster> FreeBusters = Busters.FindAll(f => f.TrackedGhost == null);
        List<Ghost> GhostsAvailable = Ghosts.FindAll(f => f.Hidden == false && f.Dead == false && f.Trapped == false && f.Position != null);
        GhostsAvailable = GhostsAvailable.FindAll(f => !Busters.FindAll(g => g.TrackedGhost != null).Exists(h => h.TrackedGhost == f));

        foreach (var buster in FreeBusters)
        {
            if(GhostsAvailable.Count == 0)
            {
                break;
            }

            buster.TrackedGhost = GhostsAvailable.OrderBy(f => GetDistance(buster.Position, f.Position)).First();
            GhostsAvailable.Remove(buster.TrackedGhost);
            Console.Error.WriteLine("Assign ghost " + buster.TrackedGhost.Id + " to buster " + buster.Id);
        }    
    }

    public double GetDistance(Coordinate position1, Coordinate position2)
    {
        return Math.Sqrt(Math.Pow(position1.X - position2.X, 2) + Math.Pow(position1.Y - position2.Y, 2));
    }
}

public abstract class Entity
{
    public int Id { get; set; }
    public Coordinate Position { get; set; }

    public Entity(int id)
    {
        Id = id;
        Position = null;
    }
}

public class Buster : Entity
{
    public BusterState State { get; set; }
    public BusterManager Manager { get; set;}
    public IBrain Brain { get; set; }
    public Ghost TrackedGhost { get; set; }
    public int StunCounter { get; set; }

    public Buster(BusterManager manager, int id) : base(id)
    {
        State = BusterState.Explore;
        Manager = manager;
        Position = manager.Base;
        Brain = new DefaultBrain(manager,this);
        TrackedGhost = null;
        StunCounter = 0;
    }

    public string NextAction()
    {
        return Brain.Think();
    }
}

public class Opponent : Entity
{
    public bool Hidden { get; set; }

    public Opponent(int id) : base(id)
    {
        Hidden = true;
    }
}


public class Ghost : Entity
{
    public bool Dead { get; set; }
    public bool Hidden { get; set; }
    public int BusterNumber { get; set; }
    public bool Trapped { get; set; }

    public Ghost(int id) : base(id)
    {
        Dead = false;
        Hidden = true;
        Position = null;
        BusterNumber = 0;
        Trapped = false;
    }
}

public class Coordinate
{
    public int X { get; set; }
    public int Y { get; set; }

    public Coordinate(int x = 0, int y = 0)
    {
        X = x;
        Y = y;
    }
}

public interface IBrain
{
    string Think();
}

public class DefaultBrain : IBrain
{
    public BusterManager Manager { get; set; }
    public Buster Buster { get; set; } 
    public Coordinate Target { get; set; }
    public string Action;
    public Coordinate LastTarget { get; set; }
    public string LastAction { get; set; }
    public double TargetDistance
    {
        get
        {
            if (Target != null
                && Buster.Position != null)
            {
                return GetDistance(Buster.Position, Target);
            }
            else
            {
                return 0;
            }
        }
    }
    private int _opponentId;

    public DefaultBrain(BusterManager manager, Buster buster)
    {
        Manager = manager;
        Buster = buster;
        Action = Constants.Move;
        Target = RandomMove();
        _opponentId = 0;
    }
    public string Think()
    {
        Explore();
        TrackGhost();
        Stun();
        string order = FormatOrder();
        Conclusion();
        Console.Error.WriteLine(Action);
        Console.Error.WriteLine(Buster.State);
        Console.Error.WriteLine("Target Position: " + Target.X + " " + Target.Y);
        Console.Error.WriteLine(order);
        Console.Error.WriteLine("--------------");

        return order;
    }

    private void TrackGhost()
    {
        if (Buster.TrackedGhost != null)
        {
            Buster.State = BusterState.Chase;
            if (Buster.TrackedGhost.Trapped)
            {
                Target = Manager.Base;
                if (TargetDistance < 1600)
                {
                    Action = Constants.Release;
                    Buster.TrackedGhost.Dead = true;
                }
                else
                {
                    Action = Constants.Move;
                }
            }
            else
            {
                Target = Buster.TrackedGhost.Position;
                if (TargetDistance < 1760
                    && TargetDistance > 900)
                {
                    Buster.State = BusterState.Explore;
                    Action = Constants.Bust;
                }
                else if (TargetDistance < 900)
                {
                    Buster.State = BusterState.Explore;
                    Target = RandomMove();
                }
            }
        }
    }

    private void Stun()
    {
        Buster.StunCounter--;
        if (Buster.StunCounter <= 0)
        {
            if (Manager.Opponents.Exists(f =>
             {
                 if (f.Position != null)
                 {
                     return GetDistance(Buster.Position, f.Position) < 1760;
                 }
                 return false;
             }))
            {
                _opponentId = Manager.Opponents.Find(f =>
                {
                    if (f.Position != null)
                    {
                        return GetDistance(Buster.Position, f.Position) < 1760;
                    }
                    return false;
                }).Id;
                LastAction = Action;
                Action = Constants.Stun;
                Buster.StunCounter = 20;
            }
        }
    }

    private void Explore()
    {
        Buster.State = BusterState.Explore;

        if (TargetDistance < 0.1)
        {
            Action = Constants.Move;
            Target = RandomMove();
        }
    }

    private void Conclusion()
    {
        if (Action == Constants.Stun)
        {
            Action = LastAction;
        }
        else if(Action == Constants.Release)
        {
            Action = Constants.Move;
            Buster.TrackedGhost = null;
        }
        else if (Action == Constants.Bust)
        {
            Action = Constants.Move;
        }
    }

    public Ghost GetClosestGhost()
    {
        List<Ghost> FoundGhosts = Manager.Ghosts.FindAll(f => f.Hidden == false && f.Dead == false);
        return FoundGhosts.OrderBy(f => GetDistance(Buster.Position, f.Position)).FirstOrDefault();
    }

    public Coordinate RandomMove()
    {
        Thread.Sleep(1);
        Random random = new Random();
        return new Coordinate(random.Next(16000), random.Next(9000));
    }

    public string Move(Coordinate position)
    {
        return Constants.Move + " " + position.X + " " + position.Y;
    }

    public double GetDistance(Coordinate position1, Coordinate position2)
    {
        return Math.Sqrt(Math.Pow(position1.X - position2.X, 2) + Math.Pow(position1.Y - position2.Y, 2));
    }

    public string FormatOrder()
    {
        string order = string.Empty;
        if (Action == Constants.Bust)
        {
            return Action + " " + Buster.TrackedGhost.Id;
        }
        else if(Action == Constants.Stun)
        {
            return Action + " " + _opponentId;
        }
        else if (Action == Constants.Move)
        {
            return Action + " " + Target.X + " " + Target.Y;
        }
        else
        {
            return Action;
        }
        
    }
}

public static class Constants
{
    public static readonly string Move = "MOVE";
    public static readonly string Bust = "BUST";
    public static readonly string Release = "RELEASE";
    public static readonly string Stun = "STUN";
}

#region Enum
public enum Strategy
{
    Default
}

public enum BusterState
{
    Explore,
    Chase,
    Default
}

public enum EntityType
{
    TeamMate,
    Opponent,
    Ghost
}
#endregion
