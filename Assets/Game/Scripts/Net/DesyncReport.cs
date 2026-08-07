// Local diagnostic dump on peer kick.
//
// When LockstepEngine kicks a peer for hash divergence or a stall,
// this writes the last ~10 s of PlayerSimState snapshots to a file
// under Application.persistentDataPath so a developer can post-mortem
// why the sim diverged. Non-blocking (fire-and-forget), and swallowed
// on IO exceptions — the goal is to help diagnose, not to gate the
// running game on filesystem success.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LifeIsAGame.DetSim;
using UnityEngine;

namespace LifeIsAGame.Net
{
    public static class DesyncReport
    {
        public static void Dump(byte playerId, uint atTick, string reason, Queue<PlayerSimState[]> snapshots)
        {
            try
            {
                string dir  = Path.Combine(Application.persistentDataPath, "desync");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir,
                    $"desync-p{playerId}-t{atTick}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");

                var sb = new StringBuilder(4096);
                sb.AppendLine($"Kicked player {playerId} at tick {atTick}. Reason: {reason}");
                sb.AppendLine($"UTC:   {DateTime.UtcNow:o}");
                sb.AppendLine($"Snapshots: last {snapshots.Count} ticks, most recent first");
                sb.AppendLine();

                int i = 0;
                foreach (var snap in snapshots)
                {
                    sb.Append($"--- snapshot #{i++} ---\n");
                    for (int p = 0; p < snap.Length; p++)
                    {
                        var s = snap[p];
                        sb.AppendLine(
                            $"  player {s.PlayerId} tick {s.LastInputTick} pos={s.Pos} vel={s.Vel} " +
                            $"yaw={s.YawRad} hp={s.Health} flags=0x{s.Flags:X2}");
                    }
                }

                File.WriteAllText(file, sb.ToString());
                Debug.Log($"[DesyncReport] wrote {file}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DesyncReport] dump failed: {e.Message}");
            }
        }
    }
}
