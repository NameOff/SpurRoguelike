﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
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

        public abstract Turn Tick();

        public abstract void GoToState<TState>(Func<TState> factory) where TState : State<T>;

        protected T Self;
    }

    public class PlayerBot : IPlayerController
    {
        private readonly int panicHealthLimit;
        private State<PlayerBot> state;
        private LevelView levelView;
        public int Level;
        public readonly int MaxLevel;

        public PlayerBot()
        {
            Level = 1;
            panicHealthLimit = 70;
            MaxLevel = 5;
            state = new StateIdle(this);
        }

        public Turn MakeTurn(LevelView levelView, IMessageReporter messageReporter)
        {
            this.levelView = levelView;
            return state.Tick();
        }

        private StepDirection GetNextStepDirection(List<Location> path)
        {
            if (path == null)
                throw new ArgumentNullException();
            var nextLocation = path.First();
            return DetermineStepDirection(nextLocation);
        }

        private StepDirection DetermineStepDirection(Location newLocation)
        {
            var playerLocation = levelView.Player.Location;
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

        private Location GetExit()
        {
            return levelView.Field.GetCellsOfType(CellType.Exit).First();
        }

        private Location[] GetNeighbors(Location location)
        {
            return Offset.StepOffsets
                .Select(offset => location + offset)
                .Where(IsLocationInRange)
                .ToArray();
        }

        private double CalculateItemValue(ItemView item)
        {
            return item.AttackBonus + item.DefenceBonus * 1.2;
        }

        private ItemView FindBestItem()
        {
            return levelView.Items.OrderBy(CalculateItemValue).Last();
        }

        private List<Location> CreatePath(Dictionary<Location, Location> dict, Location end)
        {
            var result = new List<Location>();
            var loc = end;
            while (dict[loc] != end)
            {
                result.Add(loc);
                loc = dict[loc];
            }
            result.Reverse();
            return result;
        }

        private bool IsPossibleForMove(Location location)
        {
            if (levelView.Field[location] == CellType.Trap || levelView.Field[location] == CellType.Wall)
                return false;
            return !levelView.GetMonsterAt(location).HasValue && !levelView.GetItemAt(location).HasValue;
        }

        private bool IsSafe(Location location)
        {
            if (!IsPossibleForMove(location))
                return false;
            var dangerousLocations = GetMonstersAttackingLocations();
            return !dangerousLocations.Contains(location);
        }

        private Location FindNearestMonster()
        {
            var playerLocation = levelView.Player.Location;
            return playerLocation +
                   levelView.Monsters.Select(monster => monster.Location)
                       .Select(loc => loc - playerLocation)
                       .OrderBy(offset => Math.Abs(offset.XOffset) + Math.Abs(offset.YOffset))
                       .First();
        }

        private Location FindNearestHealthPack()
        {
            var playerLocation = levelView.Player.Location;
            return playerLocation +
                   levelView.HealthPacks.Select(health => health.Location)
                       .Select(loc => loc - playerLocation)
                       .OrderBy(offset => Math.Abs(offset.XOffset) + Math.Abs(offset.YOffset))
                       .First();
        }

        private IEnumerable<Location> GetPassableLocations()
        {
            var neighbors = GetNeighbors(levelView.Player.Location);
            return neighbors
                .Where(IsPossibleForMove);
        }

        private List<Location> GetSafePath(Location end)
        {
            return GetPath(end, IsSafe);
        }

        private List<Location> GetShortestPath(Location end)
        {
            return GetPath(end, IsPossibleForMove);
        }

        private AttackDirection GetAttackDirectionFrom(Offset offset)
        {
            foreach (AttackDirection direction in Enum.GetValues(typeof(AttackDirection)))
            {
                if (Offset.FromDirection(direction) == offset)
                    return direction;
            }
            throw new ArgumentException();
        }

        private Location GetSafestLoaction(IEnumerable<Location> locations)
        {
            return locations
                .OrderBy(
                    loc =>
                        Offset.AttackOffsets.Select(offset => loc + offset)
                            .Count(location => levelView.GetMonsterAt(location).HasValue))
                .First();
        }

        private AttackDirection[] MonstersInPlayerRange()
        {
            var playerLocation = levelView.Player.Location;
            return levelView.Monsters.Select(monster => monster.Location - playerLocation)
                    .Intersect(Offset.AttackOffsets)
                    .Select(GetAttackDirectionFrom)
                    .ToArray();
        }

        private List<Location> GetAdjacentLocations(Location location)
        {
            return Offset.AttackOffsets
                .Select(offset => location + offset)
                .ToList();
        }

        private bool IsLocationInRange(Location location)
        {
            return location.X >= 0 && location.X < levelView.Field.Width &&
                location.Y >= 0 && location.Y < levelView.Field.Height;
        }

        private List<Location> GetAdjacentMonsters()
        {
            var playerLocation = levelView.Player.Location;
            return
                levelView.Monsters.Select(monster => monster.Location)
                    .Where(loc => IsInAttackRange(playerLocation, loc))
                    .ToList();
        }

        private List<Location> GetSortedHealthPackLocations()
        {
            var playerLocation = levelView.Player.Location;
            return levelView.HealthPacks.Select(health => health.Location)
                .Select(loc => loc - playerLocation)
                .OrderBy(offset => Math.Abs(offset.XOffset) + Math.Abs(offset.YOffset))
                .Select(offset => playerLocation + offset)
                .ToList();
        }

        private List<PawnView> GetMonstersInSightRadius(int sightRadius)
        {
            return levelView.Monsters
                .Where(monster => levelView.Player.Location.IsInRange(monster.Location, sightRadius))
                .ToList();
        }

        private HashSet<Location> GetMonstersAttackingLocations()
        {
            var monstersAtackingLocations = levelView.Monsters
                .Select(monster => monster.Location)
                .SelectMany(GetAdjacentLocations)
                .Where(IsLocationInRange);
            return new HashSet<Location>(monstersAtackingLocations);
        }

        private int CalculateLocationCost(Location location, HashSet<Location> monsterAttackLocations)
        {
            var cost = 1;
            if (monsterAttackLocations.Contains(location))
                cost += 10;
            if (levelView.GetMonsterAt(location).HasValue || levelView.GetItemAt(location).HasValue
                || levelView.Field[location] == CellType.Trap || levelView.Field[location] == CellType.Wall)
                cost += 100000;
            return cost;
        }

        private Tuple<Dictionary<Location, int>, Dictionary<Location, Location>> Dijkstra()
        {
            var start = levelView.Player.Location;
            var dict = new Dictionary<Location, int>();
            for (var x = 0; x < levelView.Field.Width; x++)
                for (var y = 0; y < levelView.Field.Height; y++)
                    dict[new Location(x, y)] = int.MaxValue;
            dict[start] = 0;
            var previous = new Dictionary<Location, Location> { [start] = start };
            var queue = new Queue<Location>();
            var visited = new HashSet<Location>();
            var monsterAttackLocations = new HashSet<Location>(GetMonstersAttackingLocations());
            queue.Enqueue(start);
            while (queue.Any())
            {
                var current = queue.Dequeue();
                if (visited.Contains(current))
                    continue;
                var neighbors = GetNeighbors(current).OrderBy(loc => dict[loc]);
                foreach (var neighbor in neighbors)
                {
                    if (visited.Contains(neighbor))
                        continue;
                    var cost = CalculateLocationCost(neighbor, monsterAttackLocations);
                    if (dict[neighbor] > cost + dict[current])
                    {
                        dict[neighbor] = cost + dict[current];
                        previous[neighbor] = current;
                    }
                    queue.Enqueue(neighbor);
                }
                visited.Add(current);
            }
            dict.Remove(start);
            return Tuple.Create(dict, previous);
        }

        private List<Location> GetPath(Location end, Func<Location, bool> isPossibleForMoveLocation)
        {
            var start = levelView.Player.Location;
            var dict = new Dictionary<Location, Location>();
            var queue = new Queue<Location>();
            queue.Enqueue(start);
            dict[start] = end;
            while (queue.Any())
            {
                var currentLocation = queue.Dequeue();
                var neighbors = GetNeighbors(currentLocation);
                foreach (var neighbor in neighbors)
                {
                    if (neighbor == end)
                    {
                        dict[neighbor] = currentLocation;
                        return CreatePath(dict, end);
                    }
                    if (dict.ContainsKey(neighbor) || !isPossibleForMoveLocation(neighbor))
                        continue;
                    dict[neighbor] = currentLocation;
                    queue.Enqueue(neighbor);
                }
            }
            return null;
        }

        private Location GetWeakestMonster(List<Location> monstersLocations)
        {
            return monstersLocations
                .Select(levelView.GetMonsterAt)
                .OrderBy(monster => monster.Health)
                .First().Location;
        }

        private bool IsInAttackRange(Location a, Location b)
        {
            return a.IsInRange(b, 1);
        }

        private class StateIdle : State<PlayerBot>
        {

            public StateIdle(PlayerBot self) : base(self)
            {
            }

            public override Turn Tick()
            {
                List<Location> path;
                var monstersInAttackRange = Self.MonstersInPlayerRange();
                if (monstersInAttackRange.Length == 1)
                {
                    GoToState(() => new StateAttacking(Self));
                    return Self.state.Tick();
                }
                if (monstersInAttackRange.Length > 1)
                {
                    GoToState(() => new StateCowering(Self));
                    return Self.state.Tick();
                }
                if (Self.levelView.Player.Health < Self.panicHealthLimit && Self.levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateFear(Self));
                    return Self.state.Tick();
                }
                var bestItem = Self.FindBestItem();
                ItemView item;
                if (!Self.levelView.Player.TryGetEquippedItem(out item) || Self.CalculateItemValue(item) < Self.CalculateItemValue(bestItem))
                {
                    path = Self.GetShortestPath(bestItem.Location);
                    if (path != null)
                        return Turn.Step(Self.GetNextStepDirection(path));
                }
                if (Self.levelView.Player.Health != 100 && Self.levelView.HealthPacks.Any())
                {
                    path = Self.GetShortestPath(Self.FindNearestHealthPack());
                    return Turn.Step(Self.GetNextStepDirection(path));
                }
                if (!Self.levelView.Monsters.Any())
                {
                    path = Self.GetShortestPath(Self.GetExit());
                    if (path != null && path.Count == 1)
                        Self.Level += 1;
                    return path == null ? Turn.None : Turn.Step(Self.GetNextStepDirection(path));
                }
                path = Self.GetShortestPath(Self.FindNearestMonster()) ?? Self.GetShortestPath(Self.GetExit());
                //return path == null ? Turn.None : Turn.Step(Self.GetNextStepDirection(path));
                return path == null ? Turn.None : Turn.Step(Self.GetNextStepDirection(path));
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

            public override Turn Tick()
            {
                if (Self.levelView.Player.Health < Self.panicHealthLimit && Self.levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateFear(Self));
                    return Self.state.Tick();
                }
                var monstersInAttackRange = Self.MonstersInPlayerRange();
                if (monstersInAttackRange.Length == 1)
                {
                    GoToState(() => new StateAttacking(Self));
                    return Self.state.Tick();
                }
                if (monstersInAttackRange.Length > 1)
                {
                    var dijkstra = Self.Dijkstra();
                    var values = dijkstra.Item1;
                    var sortedLocationCosts = values.Keys.OrderBy(key => dijkstra.Item1[key]);
                    var previous = dijkstra.Item2;
                    if (Self.levelView.HealthPacks.Any())
                    {
                        var best = Self.FindNearestHealthPack();
                        var min = int.MaxValue;
                        foreach (var health in Self.levelView.HealthPacks)
                        {
                            if (values[health.Location] < min)
                            {
                                min = values[health.Location];
                                best = health.Location;
                            }
                        }
                        List<Location> path;
                        if (min < 100000)
                        {
                            previous[Self.levelView.Player.Location] = best;
                            path = Self.CreatePath(previous, best);
                            return Turn.Step(Self.GetNextStepDirection(path));
                        }
                    }
                    var monsters = Self.GetAdjacentMonsters();
                    var location = Self.GetWeakestMonster(monsters);
                    var direction = Self.GetAttackDirectionFrom(location - Self.levelView.Player.Location);
                    return Turn.Attack(direction);
                }
                GoToState(() => new StateIdle(Self));
                return Self.state.Tick();
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

            public override Turn Tick()
            {
                if (Self.levelView.Player.Health < Self.panicHealthLimit && Self.levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateFear(Self));
                    Self.state.Tick();
                }
                var monstersInAttackRange = Self.MonstersInPlayerRange();
                if (monstersInAttackRange.Length == 1)
                {
                    return Turn.Attack(monstersInAttackRange[0]);
                }
                if (monstersInAttackRange.Length > 1)
                {
                    GoToState(() => new StateCowering(Self));
                    return Self.state.Tick();
                }
                GoToState(() => new StateIdle(Self));
                return Self.state.Tick();
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

            public override Turn Tick()
            {
                if (Self.levelView.Player.Health > Self.panicHealthLimit || !Self.levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateIdle(Self));
                    return Self.state.Tick();
                }
                var dijkstra = Self.Dijkstra();
                var costs = dijkstra.Item1;
                var previous = dijkstra.Item2;
                var best = Self.FindNearestHealthPack();
                var min = int.MaxValue;
                foreach (var health in Self.levelView.HealthPacks)
                {
                    if (costs[health.Location] < min)
                    {
                        min = costs[health.Location];
                        best = health.Location;
                    }
                }
                List<Location> path;
                if (min < 100000)
                {
                    previous[Self.levelView.Player.Location] = best;
                    path = Self.CreatePath(previous, best);
                }
                else
                    path = Self.GetShortestPath(Self.FindNearestHealthPack());
                return path == null ? Turn.None : Turn.Step(Self.GetNextStepDirection(path));
            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }
    }
}
