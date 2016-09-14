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
        private Entity objective;

        public PlayerBot()
        {
            panicHealthLimit = 70;
            state = new StateIdle(this);
        }

        public Turn MakeTurn(LevelView levelView, IMessageReporter messageReporter)
        {
            //Thread.Sleep(100);
            return state.Tick(levelView);
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

        private static StepDirection GetNextStepDirectionTo(Location location, LevelView levelView)
        {
            var path = GetPath(levelView.Player.Location, location, levelView);
            var nextLocation = path.First();
            return DetermineStepDirection(levelView.Player.Location, nextLocation);
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



        private static bool IsSafe(Location location, LevelView levelView)
        {
            if (levelView.Field[location] == CellType.Trap || levelView.Field[location] == CellType.Wall)
                return false;
            return levelView.Monsters.All(monster => monster.Location != location) &&
                   levelView.Items.All(item => item.Location != location);
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

        private static HashSet<Location> GetMonstersAttackingLocations(LevelView levelView)
        {
            var monstersAtackingLocations = levelView.Monsters
                .Select(monster => monster.Location)
                .SelectMany(GetAdjacentLocations)
                .Where(loc => IsLocationInRange(loc, levelView));
            return new HashSet<Location>(monstersAtackingLocations);
        }

        private static List<Location> GetPath(Location start, Location end, LevelView levelView)
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
                    if (visited.Contains(neighbor) || !IsSafe(neighbor, levelView))
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
                var bestItem = FindBestItem(levelView);
                ItemView item;
                StepDirection direction;
                if (!levelView.Player.TryGetEquippedItem(out item) || CalculateItemValue(item) < CalculateItemValue(bestItem))
                {
                    direction = GetNextStepDirectionTo(bestItem.Location, levelView);
                    return Turn.Step(direction);
                }
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
                if (levelView.Player.Health < 90 && levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateFear(Self));
                    return Self.state.Tick(levelView);
                }
                if (!levelView.Monsters.Any())
                {
                    direction = GetNextStepDirectionTo(GetExit(levelView), levelView);
                    return Turn.Step(direction);
                }
                direction = GetNextStepDirectionTo(FindNearestMonster(levelView), levelView);
                return Turn.Step(direction);
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
                    var monstersAttackingLocations = GetMonstersAttackingLocations(levelView);
                    var neighbors = GetNeighbors(levelView.Player.Location, levelView)
                        .Where(loc => IsSafe(loc, levelView) && !monstersAttackingLocations.Contains(loc))
                        .ToList();
                    if (neighbors.Any())
                        return Turn.Step(DetermineStepDirection(levelView.Player.Location, neighbors[0]));
                    else
                    {
                        if (levelView.Player.Health < Self.panicHealthLimit && levelView.HealthPacks.Any())
                        {
                            GoToState(() => new StateFear(Self));
                            return Self.state.Tick(levelView);
                        }
                        var monsters = GetAdjacentMonsters(levelView);
                        var loc = GetWeakestMonster(monsters, levelView);
                        var direction = GetAttackDirectionFrom(loc - levelView.Player.Location);
                        return Turn.Attack(direction);
                    }
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
                if (levelView.Player.Health > 90)
                {
                    GoToState(() => new StateIdle(Self));
                    return Self.state.Tick(levelView);
                }
                var direction = GetNextStepDirectionTo(FindNearestHealthPack(levelView), levelView);
                return Turn.Step(direction);
            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }

        private class StateGoToObjective : State<PlayerBot>
        {
            public StateGoToObjective(PlayerBot self) : base(self)
            {
            }

            public override Turn Tick(LevelView levelView)
            {
                throw new NotImplementedException();
            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }
    }
}
