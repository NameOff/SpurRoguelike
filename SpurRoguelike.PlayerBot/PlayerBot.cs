using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SpurRoguelike.Core;
using SpurRoguelike.Core.Entities;
using SpurRoguelike.Core.Primitives;
using SpurRoguelike.Core.Views;

namespace SpurRoguelike.PlayerBot
{
    internal abstract class State<T> where T : IPlayerController
    {
        protected State(T self)
        {
            Self = self;
        }

        public abstract Turn Tick(LevelView levelView);

        public abstract void GoToState<TState>(Func<TState> factory) where TState : State<T>;

        protected T Self;
    }

    public class PlayerBot : IPlayerController
    {
        private readonly int panicHealthLimit;
        private State<PlayerBot> state;
        public int Level;

        public PlayerBot()
        {
            Level = 1;
            panicHealthLimit = 65;
            state = new StateIdle(this);
        }

        public Turn MakeTurn(LevelView levelView, IMessageReporter messageReporter)
        {
            //Thread.Sleep(100);
            return state.Tick(levelView);
        }

        private static StepDirection GetNextStepDirection(List<Location> path, LevelView levelView)
        {
            if (path == null)
                throw new ArgumentNullException();
            var nextLocation = path.First();
            return DetermineStepDirection(levelView.Player.Location, nextLocation);
        }

        private static StepDirection DetermineStepDirection(Location playerLocation, Location newLocation)
        {
            var offset = newLocation - playerLocation;
            var direction = new Dictionary<Offset, StepDirection>
            {
                [new Offset(-1, 0)] = StepDirection.West,
                [new Offset(1, 0)] = StepDirection.East,
                [new Offset(0, 1)] = StepDirection.South,
                [new Offset(0, -1)] = StepDirection.North
            };
            return direction[offset];
        }

        private static Location GetExit(LevelView levelView)
        {
            return levelView.Field.GetCellsOfType(CellType.Exit).First();
        }

        private static Location[] GetNeighbors(Location location, LevelView levelView)
        {
            var possibleOffsets = new[] { new Offset(-1, 0), new Offset(0, 1), new Offset(1, 0), new Offset(0, -1) };
            return possibleOffsets
                .Select(offset => location + offset)
                .Where(loc => IsLocationInRange(loc, levelView))
                .ToArray();
        }

        private static double CalculateItemValue(ItemView item)
        {
            return item.AttackBonus + item.DefenceBonus * 1.2;
        }

        private static ItemView FindBestItem(LevelView levelView)
        {
            return levelView.Items.OrderBy(CalculateItemValue).Last();
        }

        private static List<Location> CreatePath(Dictionary<Location, Location> dict, Location end)
        {
            var result = new List<Location>();
            var loc = end;
            while (dict.ContainsKey(loc))
            {
                result.Add(loc);
                loc = dict[loc];
            }
            result.Reverse();
            return result;
        }

        private static bool IsPossibleForMove(Location location, LevelView levelView)
        {
            if (levelView.Field[location] == CellType.Trap || levelView.Field[location] == CellType.Wall)
                return false;
            return levelView.Monsters.All(monster => monster.Location != location) &&
                   levelView.Items.All(item => item.Location != location);
        }

        private static bool IsSafe(Location location, LevelView levelView)
        {
            if (!IsPossibleForMove(location, levelView))
                return false;
            var dangerousLocations = GetMonstersAttackingLocations(levelView);
            return !dangerousLocations.Contains(location);
        }

        private static Location FindNearestMonster(LevelView levelView)
        {
            var playerLocation = levelView.Player.Location;
            return playerLocation +
                   levelView.Monsters.Select(monster => monster.Location)
                       .Select(loc => loc - playerLocation)
                       .OrderBy(offset => Math.Abs(offset.XOffset) + Math.Abs(offset.YOffset))
                       .First();
        }

        private static Location FindNearestHealthPack(LevelView levelView)
        {
            var playerLocation = levelView.Player.Location;
            return playerLocation +
                   levelView.HealthPacks.Select(health => health.Location)
                       .Select(loc => loc - playerLocation)
                       .OrderBy(offset => Math.Abs(offset.XOffset) + Math.Abs(offset.YOffset))
                       .First();
        }

        private static List<Location> GetSafePath(Location start, Location end, LevelView levelView)
        {
            return GetPath(start, end, levelView, IsSafe);
        }

        private static List<Location> GetShortestPath(Location start, Location end, LevelView levelView)
        {
            return GetPath(start, end, levelView, IsPossibleForMove);
        }

        private static AttackDirection GetAttackDirectionFrom(Offset offset)
        {
            foreach (AttackDirection direction in Enum.GetValues(typeof(AttackDirection)))
            {
                if (Offset.FromDirection(direction) == offset)
                    return direction;
            }
            throw new ArgumentException();
        }

        private static AttackDirection[] MonstersInPlayerRange(LevelView levelView)
        {
            var playerLocation = levelView.Player.Location;
            return levelView.Monsters.Select(monster => monster.Location - playerLocation)
                    .Intersect(Offset.AttackOffsets)
                    .Select(GetAttackDirectionFrom)
                    .ToArray();
        }

        private static List<Location> GetAdjacentLocations(Location location)
        {
            return Offset.AttackOffsets
                .Select(offset => location + offset)
                .ToList();
        }

        private static bool IsLocationInRange(Location location, LevelView levelView)
        {
            return location.X >= 0 && location.X < levelView.Field.Width &&
                location.Y >= 0 && location.Y < levelView.Field.Height;
        }

        private static List<Location> GetAdjacentMonsters(LevelView levelView)
        {
            var playerLocation = levelView.Player.Location;
            return
                levelView.Monsters.Select(monster => monster.Location)
                    .Where(loc => IsInAttackRange(playerLocation, loc))
                    .ToList();
        }

        private static List<Location> GetSortedHealthPackLocations(LevelView levelView)
        {
            var playerLocation = levelView.Player.Location;
            return levelView.HealthPacks.Select(health => health.Location)
                .Select(loc => loc - playerLocation)
                .OrderBy(offset => Math.Abs(offset.XOffset) + Math.Abs(offset.YOffset))
                .Select(offset => playerLocation + offset)
                .ToList();
        }

        private static List<PawnView> GetMonstersInSightRadius(int sightRadius, LevelView levelView)
        {
            return levelView.Monsters
                .Where(monster => levelView.Player.Location.IsInRange(monster.Location, sightRadius))
                .ToList();
        }

        private static HashSet<Location> GetMonstersAttackingLocations(LevelView levelView)
        {
            var monstersAtackingLocations = levelView.Monsters
                .Select(monster => monster.Location)
                .SelectMany(GetAdjacentLocations)
                .Where(loc => IsLocationInRange(loc, levelView));
            return new HashSet<Location>(monstersAtackingLocations);
        }

        private static List<Location> GetPath(Location start, Location end, LevelView levelView, Func<Location, LevelView, bool> isPossibleForMoveLocation)
        {
            var dict = new Dictionary<Location, Location>();
            var queue = new Queue<Location>();
            queue.Enqueue(start);
            var visited = new HashSet<Location> { start };
            while (queue.Any())
            {
                var currentLocation = queue.Dequeue();
                var neighbors = GetNeighbors(currentLocation, levelView);
                foreach (var neighbor in neighbors)
                {
                    if (neighbor == end)
                    {
                        dict[neighbor] = currentLocation;
                        return CreatePath(dict, end);
                    }
                    if (visited.Contains(neighbor) || !isPossibleForMoveLocation(neighbor, levelView))
                        continue;
                    dict[neighbor] = currentLocation;
                    queue.Enqueue(neighbor);
                    visited.Add(neighbor);
                }
            }
            return null;
        }

        private static Location GetWeakestMonster(List<Location> monstersLocations, LevelView levelView)
        {
            return monstersLocations
                .Select(levelView.GetMonsterAt)
                .OrderBy(monster => monster.Health)
                .First().Location;
        }

        private static bool IsInAttackRange(Location a, Location b)
        {
            return a.IsInRange(b, 1);
        }

        private class StateIdle : State<PlayerBot>
        {

            public StateIdle(PlayerBot self) : base(self)
            {
            }

            public override Turn Tick(LevelView levelView)
            {
                List<Location> path;
                var monstersInAttackRange = MonstersInPlayerRange(levelView);
                if (monstersInAttackRange.Length == 1)
                {
                    GoToState(() => new StateAttacking(Self));
                    return Self.state.Tick(levelView);
                }
                if (monstersInAttackRange.Length > 1)
                {
                    GoToState(() => new StateCowering(Self));
                    return Self.state.Tick(levelView);
                }
                if (levelView.Player.Health < Self.panicHealthLimit && levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateFear(Self));
                    return Self.state.Tick(levelView);
                }
                var bestItem = FindBestItem(levelView);
                ItemView item;
                if (!levelView.Player.TryGetEquippedItem(out item) || CalculateItemValue(item) < CalculateItemValue(bestItem))
                {
                    path = GetShortestPath(levelView.Player.Location, bestItem.Location, levelView);
                    return path == null ? Turn.None : Turn.Step(GetNextStepDirection(path, levelView));
                }
                if (levelView.Player.Health != 100 && levelView.HealthPacks.Any())
                {
                    path = GetShortestPath(levelView.Player.Location, FindNearestHealthPack(levelView), levelView);
                    return Turn.Step(GetNextStepDirection(path, levelView));
                }
                if (!levelView.Monsters.Any())
                {
                    path = GetShortestPath(levelView.Player.Location, GetExit(levelView), levelView);
                    return path == null ? Turn.None : Turn.Step(GetNextStepDirection(path, levelView));
                }
                path = GetShortestPath(levelView.Player.Location, FindNearestMonster(levelView), levelView);
                return path == null ? Turn.None : Turn.Step(GetNextStepDirection(path, levelView));
            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }

        private class StateCowering : State<PlayerBot>
        {
            public StateCowering(PlayerBot self) : base(self)
            {
            }

            public override Turn Tick(LevelView levelView)
            {
                var monstersInAttackRange = MonstersInPlayerRange(levelView);
                if (monstersInAttackRange.Length == 1)
                {
                    GoToState(() => new StateAttacking(Self));
                    return Self.state.Tick(levelView);
                }
                if (monstersInAttackRange.Length > 1)
                {
                    var path = GetSafePath(levelView.Player.Location, GetExit(levelView), levelView);
                    if (path != null)
                    {
                        var dir = GetNextStepDirection(path, levelView);
                        return Turn.Step(dir);
                    }
                    var monstersAttackingLocations = GetMonstersAttackingLocations(levelView);
                    var neighbors = GetNeighbors(levelView.Player.Location, levelView)
                        .Where(loc => IsPossibleForMove(loc, levelView) && !monstersAttackingLocations.Contains(loc))
                        .ToList();
                    if (neighbors.Any())
                        return Turn.Step(DetermineStepDirection(levelView.Player.Location, neighbors[0]));
                    if (levelView.Player.Health < Self.panicHealthLimit && levelView.HealthPacks.Any())
                    {
                        foreach (var healthPackLocation in GetSortedHealthPackLocations(levelView))
                        {
                            var safePath = GetSafePath(levelView.Player.Location, healthPackLocation, levelView);
                            if (safePath != null)
                                return Turn.Step(GetNextStepDirection(safePath, levelView));
                        }
                        GoToState(() => new StateFear(Self));
                        return Self.state.Tick(levelView);
                    }
                    var monsters = GetAdjacentMonsters(levelView);
                    var location = GetWeakestMonster(monsters, levelView);
                    var direction = GetAttackDirectionFrom(location - levelView.Player.Location);
                    return Turn.Attack(direction);
                }
                GoToState(() => new StateIdle(Self));
                return Self.state.Tick(levelView);
            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }

        private class StateAttacking : State<PlayerBot>
        {
            public StateAttacking(PlayerBot self) : base(self)
            {
            }

            public override Turn Tick(LevelView levelView)
            {
                if (levelView.Player.Health < Self.panicHealthLimit && levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateFear(Self));
                    Self.state.Tick(levelView);
                }
                var monstersInAttackRange = MonstersInPlayerRange(levelView);
                if (monstersInAttackRange.Length == 1)
                {
                    return Turn.Attack(monstersInAttackRange[0]);
                }
                if (monstersInAttackRange.Length > 1)
                {
                    GoToState(() => new StateCowering(Self));
                    return Self.state.Tick(levelView);
                }
                GoToState(() => new StateIdle(Self));
                return Self.state.Tick(levelView);
            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }

        private class StateFear : State<PlayerBot>
        {
            public StateFear(PlayerBot self) : base(self)
            {
            }

            public override Turn Tick(LevelView levelView)
            {
                if (levelView.Player.Health > 90 || !levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateIdle(Self));
                    return Self.state.Tick(levelView);
                }
                var path = GetShortestPath(levelView.Player.Location, FindNearestHealthPack(levelView), levelView);
                return path == null ? Turn.None : Turn.Step(GetNextStepDirection(path, levelView));
            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }
    }
}
