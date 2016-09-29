﻿using System;
using System.Collections.Generic;
using System.Linq;
using SpurRoguelike.Core;
using SpurRoguelike.Core.Primitives;
using SpurRoguelike.Core.Views;

namespace SpurRoguelike.PlayerBot
{
    public class PlayerBot : IPlayerController
    {
        private int panicHealthLimit;
        private State<PlayerBot> state;
        private LevelView levelView;
        private Location exit;

        public PlayerBot()
        {
            panicHealthLimit = 60;
            state = new StateIdle(this);
        }

        public Turn MakeTurn(LevelView levelView, IMessageReporter messageReporter)
        {

            this.levelView = levelView;
            if (levelView.Field[levelView.Player.Location] == CellType.PlayerStart)
                exit = GetExit();
            if (IsLastLevel())
                panicHealthLimit = 50;
            //Console.ReadKey();
            return state.Tick();
        }

        private bool IsLastLevel()
        {
            return Offset.AttackOffsets
                .Select(offset => exit + offset)
                .Where(IsLocationInRange)
                .All(loc => levelView.Field[loc] == CellType.Wall);
        }

        private Turn GetNextTurn(List<Location> path)
        {
            if (path == null)
                throw new ArgumentNullException();
            return Turn.Step(path.First() - levelView.Player.Location);
        }

        private Location GetExit()
        {
            return levelView.Field.GetCellsOfType(CellType.Exit).First();
        }

        private IEnumerable<Location> GetNeighbors(Location location)
        {
            return Offset.StepOffsets
                .Select(offset => location + offset)
                .Where(IsLocationInRange)
                .ToArray();
        }

        private static double CalculateItemValue(ItemView item)
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

        private bool IsSafe(Location location)
        {
            if (!IsPossibleForMove(location))
                return false;
            return !Offset.AttackOffsets
                .Select(offset => location + offset)
                .Any(IsMonster);
        }

        private bool IsPossibleForMove(Location location)
        {
            if (levelView.Field[location] == CellType.Trap || levelView.Field[location] == CellType.Wall)
                return false;
            return !IsMonster(location) &&
                   !IsItem(location) &&
                   !IsHealthPack(location);
        }

        private bool IsPassable(Location location)
        {
            return levelView.Field[location] != CellType.Wall;
        }

        private List<Location> GetSafePath(Func<Location, bool> isTarget)
        {
            return GetPath(IsSafe, isTarget);
        }

        private List<Location> GetShortPath(Func<Location, bool> isTarget)
        {
            return GetPath(IsPossibleForMove, isTarget);
        }

        private List<Location> GetDangerousPath(Func<Location, bool> isTarget)
        {
            return GetPath(IsPassable, isTarget);
        }

        private List<Offset> MonstersInPlayerRange()
        {
            var playerLocation = levelView.Player.Location;
            return levelView.Monsters.Select(monster => monster.Location - playerLocation)
                .Intersect(Offset.AttackOffsets)
                .ToList();
        }

        private bool IsLocationInRange(Location location)
        {
            return location.X >= 0 && location.X < levelView.Field.Width &&
                   location.Y >= 0 && location.Y < levelView.Field.Height;
        }

        private IEnumerable<Location> GetAdjacentMonsters()
        {
            var playerLocation = levelView.Player.Location;
            return levelView.Monsters.Select(monster => monster.Location)
                .Where(loc => IsInAttackRange(playerLocation, loc));
        }

        private IEnumerable<Location> GetLocationsInRange(int range, Location start)
        {
            return
                Offset.AttackOffsets.SelectMany(
                    offset =>
                        new[]
                        {
                            new Offset(offset.XOffset*range, offset.YOffset),
                            new Offset(offset.XOffset, offset.YOffset*range),
                            new Offset(offset.XOffset*range, offset.YOffset*range)
                        })
                        .Select(offset => start + offset)
                        .Where(IsLocationInRange)
                        .Distinct();
        }

        private int CalculateWallWaves(Location location, int range, int cost)
        {
            return GetLocationsInRange(range, location)
                .Where(loc => levelView.Field[loc] == CellType.Wall)
                .Sum(loc => cost);
        }

        private int CalculateMonsterWaves(Location location)
        {
            var cost = 0;
            foreach (var monster in levelView.Monsters)
            {
                var offset = monster.Location - location;
                var max = offset.Size();
                if (max > 5)
                    continue;
                cost += (int)Math.Pow(2, 6 - max);
                if (Math.Abs(offset.XOffset) == 1 && Math.Abs(offset.YOffset) == 1)
                    cost += (int)Math.Pow(2, 6 - max);
            }
            return cost;
        }

        private int CalculateLocationCost(Location location)
        {
            if (IsMonster(location) || IsItem(location) ||
                levelView.Field[location] == CellType.Trap || levelView.Field[location] == CellType.Wall)
                return 100000;

            var cost = 1;
            cost += CalculateWallWaves(location, 1, 20);
            cost += CalculateWallWaves(location, 2, 15);
            cost += CalculateWallWaves(location, 3, 10);
            cost += CalculateMonsterWaves(location);
            return cost;
        }


        private Tuple<Dictionary<Location, int>, Dictionary<Location, Location>> Dijkstra()
        {
            var start = levelView.Player.Location;
            var distance = new Dictionary<Location, int>();
            var hashes = new Dictionary<int, Location>();
            for (var x = 0; x < levelView.Field.Width; x++)
                for (var y = 0; y < levelView.Field.Height; y++)
                {
                    var loc = new Location(x, y);
                    distance[loc] = int.MaxValue;
                    hashes[loc.GetHashCode()] = loc;
                }
            distance[start] = 0;
            var previous = new Dictionary<Location, Location> { [start] = start };
            var queue = new SortedSet<Tuple<int, int>>();

            var visited = new HashSet<Location>();
            queue.Add(Tuple.Create(0, start.GetHashCode()));
            var test = new Dictionary<Location, int>();
            while (queue.Any())
            {
                var node = queue.Min;
                var current = hashes[node.Item2];
                queue.Remove(node);
                if (visited.Contains(current))
                    continue;
                visited.Add(current);
                var neighbors = GetNeighbors(current);
                foreach (var neighbor in neighbors)
                {
                    if (visited.Contains(neighbor))
                        continue;
                    var cost = CalculateLocationCost(neighbor);
                    test[neighbor] = cost;
                    if (distance[neighbor] > cost + distance[current])
                    {
                        queue.Remove(Tuple.Create(distance[neighbor], neighbor.GetHashCode()));
                        distance[neighbor] = cost + distance[current];
                        previous[neighbor] = current;
                        queue.Add(Tuple.Create(distance[neighbor], neighbor.GetHashCode()));
                    }
                }
            }
            distance.Remove(start);
            return Tuple.Create(distance, previous);
        }

        private List<Location> GetPath(Func<Location, bool> isPossibleForMoveLocation, Func<Location, bool> isTarget) // BFS
        {
            var start = levelView.Player.Location;
            var dict = new Dictionary<Location, Location>();
            var queue = new Queue<Location>();
            queue.Enqueue(start);
            while (queue.Any())
            {
                var currentLocation = queue.Dequeue();
                var neighbors = GetNeighbors(currentLocation);
                foreach (var neighbor in neighbors)
                {
                    if (isTarget(neighbor))
                    {
                        dict[start] = neighbor;
                        dict[neighbor] = currentLocation;
                        return CreatePath(dict, neighbor);
                    }
                    if (dict.ContainsKey(neighbor) || !isPossibleForMoveLocation(neighbor))
                        continue;
                    dict[neighbor] = currentLocation;
                    queue.Enqueue(neighbor);
                }
            }
            return null;
        }

        private Location GetWeakestMonster(IEnumerable<Location> monstersLocations)
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

        private Turn RandomMove()
        {
            var steps = Offset.StepOffsets
                .Where(offset => IsPossibleForMove(levelView.Player.Location + offset))
                .ToList();
            return steps.Any() ? Turn.Step(steps.ElementAt(levelView.Random.Next(0, steps.Count))) : Turn.None;
        }

        private List<Location> GetPathToBestItem()
        {
            var bestItem = FindBestItem();
            ItemView item;
            if (!levelView.Player.TryGetEquippedItem(out item) || CalculateItemValue(item) < CalculateItemValue(bestItem))
            {
                var path = GetShortPath(loc => loc == bestItem.Location);
                if (path != null)
                    return path;
            }
            return null;
        }

        private bool IsHealthPack(Location location) => levelView.GetHealthPackAt(location).HasValue;

        private bool IsMonster(Location location) => levelView.GetMonsterAt(location).HasValue;

        private bool IsItem(Location location) => levelView.GetItemAt(location).HasValue;

        private bool IsExit(Location location) => location == exit;

        private List<Location> GetBestPathToHealthPack()
        {
            if (!levelView.HealthPacks.Any())
                return null;

            var healthPacksInPlayerRange = Offset.StepOffsets.Select(offset => levelView.Player.Location + offset)
                .Where(IsHealthPack)
                .ToList();
            if (healthPacksInPlayerRange.Any())
                return new List<Location> { healthPacksInPlayerRange.First() };

            var dijkstra = Dijkstra();
            var values = dijkstra.Item1;
            var previous = dijkstra.Item2;

            var healthLocation = default(Location);
            var minimumCost = int.MaxValue;
            foreach (var health in levelView.HealthPacks)
            {
                var healthPacks = Offset.StepOffsets.Select(offset => health.Location + offset).ToList();
                healthPacks.Add(health.Location);
                foreach (var packLoc in healthPacks)
                {
                    if (values[packLoc] < minimumCost)
                    {
                        minimumCost = values[packLoc];
                        healthLocation = packLoc;
                    }
                }

            }
            previous[levelView.Player.Location] = healthLocation;
            return CreatePath(previous, healthLocation);
        }

        private class StateIdle : State<PlayerBot>
        {
            private int counter;

            public StateIdle(PlayerBot self) : base(self)
            {
                counter = 0;
            }

            public override Turn Tick()
            {
                counter++;
                if (counter >= 60)
                {
                    counter = 0;
                    return Self.RandomMove();
                }

                var monstersInAttackRange = Self.MonstersInPlayerRange();
                if (monstersInAttackRange.Count == 1)
                {
                    GoToState(() => new StateAttacking(Self));
                    return Self.state.Tick();
                }
                if (monstersInAttackRange.Count > 1)
                {
                    GoToState(() => new StateCowering(Self));
                    return Self.state.Tick();
                }
                if (Self.levelView.Player.Health < Self.panicHealthLimit && Self.levelView.HealthPacks.Any())
                {
                    GoToState(() => new StateFear(Self));
                    return Self.state.Tick();
                }

                var path = Self.GetPathToBestItem();
                if (path != null)
                    return Self.GetNextTurn(path);

                if (!Self.IsLastLevel() && Self.levelView.Player.Health != 100 && Self.levelView.HealthPacks.Any())
                {
                    path = Self.GetShortPath(Self.IsHealthPack);
                    if (path != null)
                        return Self.GetNextTurn(path);
                }
                if (!Self.levelView.Monsters.Any())
                {
                    path = Self.GetShortPath(Self.IsExit);
                    if (path != null)
                        return Self.GetNextTurn(path);
                }
                path = (Self.GetShortPath(loc => Offset.AttackOffsets.Any(offset => Self.IsMonster(loc + offset)) && Self.IsPossibleForMove(loc)) ??
                        Self.GetShortPath(Self.IsExit)) ??
                        Self.GetDangerousPath(Self.IsExit);
                return Self.GetNextTurn(path);
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
                if (monstersInAttackRange.Count == 1)
                {
                    GoToState(() => new StateAttacking(Self));
                    return Self.state.Tick();
                }
                if (monstersInAttackRange.Count > 1)
                {
                    var monsters = Self.GetAdjacentMonsters();
                    if (!Offset.StepOffsets.Any(offset => Self.levelView.Field[Self.levelView.Player.Location + offset] == CellType.Empty))
                    {
                        var location = Self.GetWeakestMonster(monsters);
                        return Turn.Attack(location - Self.levelView.Player.Location);
                    }

                    var pathToHealth = Self.GetBestPathToHealthPack();
                    if (pathToHealth != null)
                        return Self.GetNextTurn(pathToHealth);

                    var path = Self.GetShortPath(loc => loc == Self.exit);
                    return Self.GetNextTurn(path);

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
                    return Self.state.Tick();
                }
                var monstersInAttackRange = Self.MonstersInPlayerRange();
                if (monstersInAttackRange.Count == 1)
                {
                    return Turn.Attack(monstersInAttackRange.First());
                }
                if (monstersInAttackRange.Count > 1)
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
                List<Location> path;
                if (Self.IsLastLevel())
                {
                    path = Self.GetSafePath(Self.IsHealthPack);
                    if (path != null)
                        return Self.GetNextTurn(path);
                    path = Self.GetShortPath(Self.IsHealthPack);
                    return Self.GetNextTurn(path);
                }
                path = Self.GetBestPathToHealthPack();
                return Self.GetNextTurn(path);

            }

            public override void GoToState<TState>(Func<TState> factory)
            {
                Self.state = factory();
            }
        }
    }

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

}
