using Newtonsoft.Json.Linq;

namespace BeatMapEvaluator
{
	/// <summary>Basic evaluation info holder struct</summary>
	public class DiffCriteriaReport {
		//Swings per second for both hands
		public int[]? LeftHandSwings, RightHandSwings;
		//R1:d
		public List<string> ModsRequired { get; set; }
		//R1:f
		public List<json_MapNote> NoteHotStarts { get; set; }
		public List<json_MapObstacle> WallHotStarts { get; set; }
		//R1:g
		public List<json_MapNote> NoteColdEnds { get; set; }
		public List<json_MapObstacle> WallColdEnds { get; set; }
		//R3:a
		public List<json_MapNote> NoteIntersections { get; set; }
		public List<json_MapObstacle> WallIntersections { get; set; }
		//R4:cd
		public List<json_MapNote> NoteOutOfRange { get; set; }
		public List<json_MapObstacle> WallOutOfRange { get; set; }
		//R3:e, R5:a
		public List<json_MapNote> NoteFailSwings { get; set; }

		//error, failed, passed
		public static readonly string[] diffColors = {"#713E93","#BE1F46","#9CED9C"};

		//Error counts
		public int[] errors { get; set; }
		/// <summary>
		/// Fills out <see cref="errors"/> with error counts per error check
		/// </summary>
		/// <returns>Daaa status enum</returns>
		public ReportStatus GetReportStatus() {
			//Preset all errors to -1 (Error failed)
			errors = new int[7];
			for(int i = 0; i < 7; i++)
				errors[i] = -1;

			//Move the list requirements to the errors array
			if(ModsRequired != null)
				errors[0] = ModsRequired.Count;
			if (NoteHotStarts != null && WallHotStarts != null)
				errors[1] = NoteHotStarts.Count + WallHotStarts.Count;
			if(NoteColdEnds != null && WallColdEnds != null)
				errors[2] = NoteColdEnds.Count + WallColdEnds.Count;
			if(NoteIntersections != null && WallIntersections != null)
				errors[3] = NoteIntersections.Count + WallIntersections.Count;
			if(NoteFailSwings != null)
				errors[4] = NoteFailSwings.Count;
			if(NoteOutOfRange != null)
				errors[5] = NoteOutOfRange.Count;
			if(WallOutOfRange != null)
				errors[6] = WallOutOfRange.Count;

			//Any of the errors are -1
			if(errors.Contains(-1)) 
				return ReportStatus.Error;
			//All are zero mean good
			if(errors.All(e => e == 0))
				return ReportStatus.Passed;
			// :/
			return ReportStatus.Failed;
		}

		/// <summary>Clears anything that has memory allocated from the struct</summary>
		public void ClearCache() {
			if(NoteHotStarts != null) NoteHotStarts.Clear();
			if(WallHotStarts != null) WallHotStarts.Clear();
			if(NoteColdEnds != null) NoteColdEnds.Clear();
			if(WallColdEnds != null) WallColdEnds.Clear();
			if(NoteIntersections != null) NoteIntersections.Clear();
			if(WallIntersections != null) WallIntersections.Clear();
			if(NoteOutOfRange != null) NoteOutOfRange.Clear();
			if(WallOutOfRange != null) WallOutOfRange.Clear();
			if(NoteFailSwings != null) NoteFailSwings.Clear();
		}
	}

	public class MapStorageLayout {
		public DiffCriteriaReport report;
		public json_beatMapDifficulty mapDiff;
		public json_DiffFileV2 diffFile;
		public json_MapInfo info;

		//??? C# moment lol
		/// <summary>The maps notes in a cache</summary>
		public Dictionary<float, List<json_MapNote>?>? noteCache;
		/// <summary>The maps walls in a cache</summary>
		public Dictionary<float, List<json_MapObstacle>?>? wallCache;
		/// <summary>The swings per second L/R arrays</summary>
		public int[]? LeftHandSwings, RightHandSwings;

		/// <summary>This maps evaluated status</summary>
		public ReportStatus reportStatus = ReportStatus.None;

		/// <summary>Total note count (excluding bombs)</summary>
		public int actualNoteCount;

		//Audio Length in seconds
		public float audioLength;
		/// <summary>60/bpm</summary>
		public float beatsPerSecond;

		public float bpm, njs;
		public float notesPerSecond;
		public float noteOffset;
		public float jumpDistance;
		public float reactionTime;

		//mmhm mmhm yep mmhm we constructing the class instance
		public MapStorageLayout(
				json_MapInfo info, 
				json_DiffFileV2 diff, 
				json_beatMapDifficulty mapDiff,
				float audioLength) {
			
			this.mapDiff = mapDiff;
			this.info = info;
			this.audioLength = audioLength;

			noteCache = new Dictionary<float, List<json_MapNote>?>();
			wallCache = new Dictionary<float, List<json_MapObstacle>?>();
			bpm = info._bpm;
			beatsPerSecond = 60f / bpm;
			njs = mapDiff._noteJumpMovementSpeed;
			notesPerSecond = 0;
			noteOffset = mapDiff._noteJumpStartBeatOffset;
			jumpDistance = Utils.CalculateJD(bpm, njs, noteOffset);
			reactionTime = Utils.CalculateRT(jumpDistance, njs);
			diffFile = diff;
		}

		/// <summary>Process the evaluation logic on this MapStorageLayout.</summary>
		public async Task ProcessDiffRegistery() {
			report = new DiffCriteriaReport();
			//report.modsRequired = await Eval_ModsRequired();

			//Preload the mods required, notes and walls
			Task[] Loaders = new Task[] {
				Eval_ModsRequired(),
				Load_NotesToCache(diffFile),
				Load_ObstaclesToCache(diffFile),
			};
			//Wait for meee
			await Task.WhenAll(Loaders);
			//Push the mod requirements early
			report.ModsRequired = ((Task<List<string>>)Loaders[0]).Result;
			//Remove all notes outside the 3x4 range
			Task[] Cullers = new Task[] {
				Eval_OutOfRangeNotes(),
				Eval_OutOfRangeWalls()
			};
			//Wait for the tasks to finish
			await Task.WhenAll(Cullers);
			//Load the Swings per second for each hand
			(int lhs, int rhs)[] swingRegistry = await Eval_SwingsPerSecond(1.0f/6.0f, 1.0f/5.0f);
			//Split the swings to their arrays
			int[] leftSwings = new int[swingRegistry.Length];
			int[] rightSwings = new int[swingRegistry.Length];
			for(int i = 0; i < swingRegistry.Length; i++) {
				leftSwings[i] = swingRegistry[i].lhs;
				rightSwings[i] = swingRegistry[i].rhs;
			}

            report.LeftHandSwings = leftSwings;
			report.RightHandSwings = rightSwings;

			report.NoteOutOfRange = ((Task<List<json_MapNote>>)Cullers[0]).Result;
			report.WallOutOfRange = ((Task<List<json_MapObstacle>>)Cullers[1]).Result;
			
			//I really. Really. Hate this. but C# loves cockblocking the thread if something goes wrong
			//and I dont actively know of any (try catch multiple) block that continues 

			try {report.NoteHotStarts = await Eval_NoteHotStart(1.33f);} 
			catch {}

			try {report.WallHotStarts = await Eval_WallHotStart(1.33f);} 
			catch {}

			try {report.NoteColdEnds = await Eval_NoteColdEnd(2.0f);} 
			catch {}

			try {report.WallColdEnds = await Eval_WallColdEnd(2.0f);} 
			catch {}

			try {report.NoteIntersections = await Eval_NoteIntersections();} 
			catch {}

			try {report.WallIntersections = await Eval_WallIntersections();} 
			catch {}

			try {report.NoteFailSwings = await Eval_FailSwings();} 
			catch {}
			
			reportStatus = report.GetReportStatus();
		}

		/// <summary>Clears the stored memory from this MapStorageLayout</summary>
		public void ClearDiff(bool callGC=true) {
			report.ClearCache();
			noteCache.Clear();
			wallCache.Clear();
			noteCache = null;
			wallCache = null;
			diffFile._notes = null;
			diffFile._walls = null;
			LeftHandSwings = null;
			RightHandSwings = null;
			mapDiff = null;
			if(callGC)
				GC.Collect(); //LMAO I FUCKING HATE C# WHYY
		}

		/// <summary>
		/// Loads all notes from <see cref="json_DiffFileV2"/> to <see cref="noteCache"/>.
		/// </summary>
		/// <param name="diff">The diffFile intermediate</param>
		public Task Load_NotesToCache(json_DiffFileV2 diff) {
			//Roud up how many seconds there are in the audio for swings/second
			int cellCount = (int)Math.Ceiling(audioLength);

			int noteCount = 0;
			foreach(var note in diff._notes) {
				note.cellIndex = 4 * note._lineLayer + note._lineIndex;
				note.realTime = note._time * beatsPerSecond;
				if(note._type != NoteType.Bomb) {
					//get curent cell index
					int index = (int)Math.Floor(note.realTime);
					noteCount++;
				}

				if(!noteCache.ContainsKey(note._time)) {
					var push = new List<json_MapNote>(){note};
					noteCache.Add(note._time, push);
				} else {
					noteCache[note._time].Add(note);
				}
			}
			//Calculate the overall notesPerSecond
			notesPerSecond = noteCount / audioLength;
			return Task.CompletedTask;
		}
		/// <summary>
		/// Loads all walls from <see cref="json_DiffFileV2"/> to <see cref="wallCache"/>.
		/// </summary>
		/// <param name="diff">The diffFile intermediate</param>
		public Task Load_ObstaclesToCache(json_DiffFileV2 diff) {
			//Smallest acceptable time for walls
			const float shortWallEpsilon = 1.0f / 72.0f;
			foreach(var wall in diff._walls) {
				wall.isInteractive = wall._lineIndex == 1 || wall._lineIndex == 2;
				wall.realTime = wall._time * beatsPerSecond;
				wall.endTime = wall._time + wall._duration;
				wall.isShort = wall.realTime < shortWallEpsilon;
				
				//No wall here? add it
				if(!wallCache.ContainsKey(wall._time)) {
					var push = new List<json_MapObstacle>(){wall};
					wallCache.Add(wall._time, push);
				} else {
					wallCache[wall._time].Add(wall);
				}
			}
			return Task.CompletedTask;
		}
		
		//R1:d
		/// <summary>
		/// Esoteric implementation of looking for json "_requirements"
		/// and checking if anything is in it
		/// </summary>
		/// <returns>List of mods in requirements</returns>
		public Task<List<string>> Eval_ModsRequired() {
			List<string> modList = new List<string>();
			JObject? customData = (JObject?)mapDiff._customData;
			if(customData != null) {
				var t = customData.SelectToken("_requirements");
				if(t != null) {
					var modCell = t.ToObject<string[]>();
					if(modCell != null)
						modList.AddRange(modCell);
				}
			}
			return Task.FromResult(modList);
		}
		//R1:f
		/// <summary>
		/// Finds notes that sit before the starting blank period. AKA "Hot start"
		/// </summary>
		/// <param name="limit">hot start time in seconds</param>
		/// <returns>List of offenders</returns>
		public Task<List<json_MapNote>> Eval_NoteHotStart(float limit) {
			List<json_MapNote> offenders = new List<json_MapNote>();
			//Get the limit beat
			float beatLimit = limit * beatsPerSecond;
			foreach(var (time, list) in noteCache) { 
				if(time < beatLimit)
					offenders.AddRange(list);
				//If the limit has passed just skip
				else break;
			}
			return Task.FromResult(offenders);
		}
		/// <summary>
		/// Finds Walls that sit before the starting blank period. AKA "Hot start"
		/// </summary>
		/// <param name="limit">hot start time in seconds</param>
		/// <returns>List of offenders</returns>
		public Task<List<json_MapObstacle>> Eval_WallHotStart(float limit) {
			List<json_MapObstacle> offenders = new List<json_MapObstacle>();
			//Get the limit beat
			float beatLimit = limit * beatsPerSecond;

			foreach(var (time, list) in wallCache) { 
				if(time < beatLimit) { 
					foreach(var wall in list) {
						//Only add to offender list if its interactive
						if(wall.isInteractive)
							offenders.Add(wall);
					}
				}
				//If the limit has passed just skip
				else break;
			}
			return Task.FromResult(offenders);
		}
		//R1:g
		/// <summary>
		/// Finds notes that exist past the given timeout period at the end, AKA "Cold Ends"
		/// </summary>
		/// <param name="limit">cold end time in seconds</param>
		/// <returns>List of offenders</returns>
		public Task<List<json_MapNote>> Eval_NoteColdEnd(float limit) {
			List<json_MapNote> offenders = new List<json_MapNote>();
			//Get the ending beat
			float kernel = (audioLength - limit) / beatsPerSecond;
			//Reverse the order of the noteCache with a stack
			Stack<float> time_rev = new Stack<float>(noteCache.Keys);
			foreach(var time in time_rev) {
				if(time >= kernel)
					offenders.AddRange(noteCache[time]);
				//If the limit has passed just skip
				else break;
			}
			return Task.FromResult(offenders);
		}
		/// <summary>
		/// Finds Walls that exist past the given timeout period at the end, AKA "Cold Ends"
		/// </summary>
		/// <param name="limit">cold end time in seconds</param>
		/// <returns>List of offenders</returns>
		public Task<List<json_MapObstacle>> Eval_WallColdEnd(float limit) {
			List<json_MapObstacle> offenders = new List<json_MapObstacle>();
			//Get the ending beat
			float kernel = (audioLength - limit) / beatsPerSecond;
			//Reverse the order of the wallCache with a stack
			Stack<float> time_rev = new Stack<float>(wallCache.Keys);

			//Add only interactive walls
			foreach(var time in time_rev) { 
				if(time >= kernel) { 
					foreach(var wall in wallCache[time]) {
						if(wall.isInteractive) {
							offenders.Add(wall);
						}
					}
				}
				//If the limit has passed just skip
				else break;       
			}
			return Task.FromResult(offenders);
		}
		//R3:a
		/// <summary>
		/// Finds notes that share the same cell on the same time step
		/// </summary>
		/// <returns>A list of offenders</returns>
		public Task<List<json_MapNote>> Eval_NoteIntersections() {
			List<json_MapNote> offenders = new List<json_MapNote>();

			foreach(var (time, list) in noteCache) { 
				bool[] used = new bool[3*4];
				foreach(var note in list) { 
					if(used[note.cellIndex])
						offenders.Add(note);
					used[note.cellIndex] = true;
				}
			}

			return Task.FromResult(offenders);
		}
		/// <summary>
		/// Finds walls that have notes or other walls inside of them
		/// </summary>
		/// <returns>A list of offenders</returns>
		public Task<List<json_MapObstacle>> Eval_WallIntersections() {
			List<json_MapObstacle> offenders = new List<json_MapObstacle>();

			foreach(var (time, list) in wallCache) { 
				foreach(json_MapObstacle wall in list) {
					//Find all note timesteps that are within the walls time domain
					var look = noteCache.Where(note => (note.Key >= time && note.Key <= wall.endTime));
					
					//Skip if no notes in range of the walls range
                    if(look.Count() == 0)
						continue;

					//Get wall info
					int wx = wall._lineIndex;
					int wSpan = wx + (wall._width - 1);
					bool isFull = wall._type == ObstacleType.FullWall;
					//Simple note inside wall check for every note thats in range
					foreach(var (noteKey, noteList) in look) {
						foreach(json_MapNote note in noteList) {
							int nx = note._lineIndex;
							int ny = note._lineLayer;
							bool inRangeX = (nx >= wx) && (nx <= wSpan);
							bool underWall = (!isFull && ny == 0);
							if(inRangeX && !underWall) {
                                offenders.Add(wall);
                            }
                        }
					}
				}
			}
			return Task.FromResult(offenders);
		}

		//R4:cd
		/// <summary>
		/// Removes all notes that have a position outside of the beat saber grid
		/// </summary>
		/// <returns>A list of offenders</returns>
		public Task<List<json_MapNote>> Eval_OutOfRangeNotes() {
			List<json_MapNote> offenders = new List<json_MapNote>();

			//Tomb array because foreach has to iterate over the array
			List<float> globalTomb = new List<float>();
			foreach(var (time, list) in noteCache) {
				//Local timestep tomb
				List<json_MapNote> tomb = new List<json_MapNote>();
				foreach(var note in list) {
					//Add all notes outside of the 3x4 space to the tomb list
					bool xBound = note._lineIndex < 0 || note._lineIndex > 3;
					bool yBound = note._lineLayer < 0 || note._lineLayer > 2;
					if(xBound || yBound)
						tomb.Add(note);
				}
				//Add all tomb items to the offenders registry
				offenders.AddRange(tomb);

				//Remove all notes outside of the 3x4 grid for this timestep
				foreach(var target in tomb)
					list.Remove(target);

				//Remove the timestep if there arent anymore blocks
				if(list.Count == 0)
					globalTomb.Add(time);
			}
			//Remove all empty timesteps
			foreach(float target in globalTomb)
				noteCache.Remove(target);
			return Task.FromResult(offenders);
		}
		/// <summary>
		/// Removes all walls that have a position outside of the beat saber grid
		/// </summary>
		/// <returns>A list of offenders</returns>
		public Task<List<json_MapObstacle>> Eval_OutOfRangeWalls() {
			List<json_MapObstacle> offenders = new List<json_MapObstacle>();

			//Tomb array because foreach has to iterate over the array
			List<float> globalTomb = new List<float>();
			foreach(var (time, list) in wallCache) {
				//Local timestep tomb
				List<json_MapObstacle> tomb = new List<json_MapObstacle>();
				foreach(var wall in list) {
					int wx = wall._lineIndex;
					int wSpan = wx + (wall._width - 1);

					bool ZeroWall = Utils.Approx(wall._duration, 0f, 0.001f);
					bool negativeWidth = wall._width < 0f;
					bool negativeDuration = wall._duration < 0f;

					bool outOfRange = wx < 0 || wx > 3 || wSpan > 3;
					bool invalid = ZeroWall || negativeWidth || negativeDuration;
					if(outOfRange || invalid)
						tomb.Add(wall);
				}
				//Add all tomb items to the offenders registry
				offenders.AddRange(tomb);

				//Remove all walls outside of the 3x4 grid for this timestep
				foreach(var target in tomb)
					list.Remove(target);

				//Remove the timestep if there arent anymore walls
				if(list.Count == 0)
					globalTomb.Add(time);
			}
			//Remove all empty timesteps
			foreach(var target in globalTomb)
				wallCache.Remove(target);

			return Task.FromResult(offenders);
		}
		//R3:e, R5:a
		/// <summary>
		/// Finds patterns that are only made by a prick, 
		/// for instance a bomb on the cut outward side of a block
		/// </summary>
		/// <returns>A list of offenders</returns>
		public Task<List<json_MapNote>> Eval_FailSwings() {
			List<json_MapNote> offenders = new List<json_MapNote>();

			foreach(var (time, list) in noteCache) { 
				foreach(var note in list) { 
					if(note._type != NoteType.Bomb) {
						//Check the cut directions cell
						var next = Utils.GetAdjacentNote(list, note, note._cutDirection);
						//not empty and not the same handedness
						if(next != null && next._type != note._type)
							offenders.Add(next);
					}
				}
			}

			return Task.FromResult(offenders);
		}

		/// <summary>
		/// Calculates the swings per second with slider logic in place.
		/// </summary>
		/// <remarks>
		/// <c>Note:</c>
		/// I just pretend multiple of the same coloured note on the same time step dont exist.
		/// </remarks>
		/// <returns>List of (left,right) swings/second</returns>
		public Task<(int, int)[]> Eval_SwingsPerSecond(float dotPrec, float sliderPrec) {
			int cellCount = (int)Math.Ceiling(audioLength);
			(int left, int right)[] swingList = new (int, int)[cellCount];

			float[] noteKeys = noteCache.Keys.ToArray();

			List<json_MapNote> lastCell = noteCache[noteKeys[0]];
			json_MapNote? lastLeft = lastCell.Find(note => note._type == NoteType.Left);
			json_MapNote? lastRight = lastCell.Find(note => note._type == NoteType.Right);
			if(lastLeft != null) swingList[0].left++;
			if(lastRight != null) swingList[0].right++;

			for (int i = 1; i < noteKeys.Length; i++) {
				//Get the second we are currently looking at for swings/second
				int swingCellIndex = (int)Math.Floor(noteKeys[i] * beatsPerSecond);
				List<json_MapNote> cell = noteCache[noteKeys[i]];

				//Find left and right blocks in the time step
				json_MapNote? left = cell.Find(note => note._type == NoteType.Left);
				json_MapNote? right = cell.Find(note => note._type == NoteType.Right);

				//if there is a block between the last and current block
				//evaluate if its part of a slider and add to the swing/second
				if(lastLeft != null && left != null) { 
					bool leftSlider = Utils.IsSlider(left, lastLeft, dotPrec, sliderPrec);
					if(!leftSlider) swingList[swingCellIndex].left++;
				} else if(left != null) {
					swingList[swingCellIndex].left++;
				}
				if(lastRight != null && right != null) { 
					bool rightSlider = Utils.IsSlider(right, lastRight, dotPrec, sliderPrec);
                    if(!rightSlider) swingList[swingCellIndex].right++;
                } else if(right != null) {
					swingList[swingCellIndex].right++;
				}

				//Set the last values to the current values
				if(lastLeft == null || left != null)
					lastLeft = left;
				if(lastRight == null || right != null)
					lastRight = right;
            }

			return Task.FromResult(swingList);
        }
	}
}
