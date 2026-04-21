using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Parser.Map;
using Parser.Map.Difficulty.V3.Base;
using Parser.Map.Difficulty.V3.Grid;

namespace RatingAPI.Controllers
{
    public class PredictedNote
    {
        public float Time { get; set; }
        public float Acc { get; set; }
        public float Pass { get; set; }
        public float Tech { get; set; }
    }
    public class PredictionResult
    {
        public float AIAcc { get; set; }
        public PredictedNote[] Notes { get; set; }
    }

    public class InferPublish
    {
        private const int BatchSize = 4;
        private const int NumThreads = 4;
        private const int preSegmentSize = 12;
        private const int postSegmentSize = 12;
        private DataProcessing dataProcessing = new DataProcessing();

        private static object inferenceSessionLock = new();
        private static InferenceSession inferenceSessionAccNew = new InferenceSession(Path.Combine(AppContext.BaseDirectory, "model_sleep_bl.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = NumThreads, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL });
        //private static InferenceSession inferenceSessionAcc = new InferenceSession(Path.Combine(AppContext.BaseDirectory, "model_sleep_4LSTM_acc.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = NumThreads, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL });
        //private static InferenceSession inferenceSessionSpeed = new InferenceSession(Path.Combine(AppContext.BaseDirectory, "model_sleep_4LSTM_speed.onnx"), new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = NumThreads, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL });
        private static InferenceSession tagSession = new InferenceSession(Path.Combine(AppContext.BaseDirectory, "tagging_model.onnx"));

        // Replace with this to use gpu. Requires Microsoft.ML.OnnxRuntime.Gpu nuget
        //private static InferenceSession inferenceSession = new InferenceSession(AppContext.BaseDirectory + "\\model_sleep_4LSTM_acc.onnx", Microsoft.ML.OnnxRuntime.SessionOptions.MakeSessionOptionWithCudaProvider());

        public int GetMultiplierForCombo(int combo)
        {
            if (combo <= 1) return 1;
            if (combo <= 5) return 2;
            if (combo <= 13) return 4;
            return 8;
        }

        public int GetMaxScoreForNotes(int noteCount)
        {
            int totalScore = 0;

            for (int i = 0; i < noteCount; i++)
            {
                int multiplier = GetMultiplierForCombo(i + 1);
                totalScore += 115 * multiplier;
            }

            return totalScore;
        }

        public void SetMapAccForHits(PredictedNote[] hits)
        {
            float maxScore = 0;
            float totalScore = 0;

            for (int i = 0; i < hits.Length; i++)
            {
                float multiplier = GetMultiplierForCombo(i + 1);
                totalScore += (hits[i].Acc * 15 + 100) * multiplier;
                maxScore += 115 * multiplier;

                hits[i].Acc = totalScore / maxScore;
            }
        }

        public double GetMapAccForHits(List<float> hits, int freePoints)
        {
            float maxScore = 0;
            float totalScore = 0;

            for (int i = 0; i < hits.Count; i++)
            {
                float multiplier = GetMultiplierForCombo(i + 1);
                totalScore += (hits[i] * 15 + 100) * multiplier;
                maxScore += 115 * multiplier;
            }

            totalScore += freePoints;
            maxScore += freePoints;

            if (maxScore == 0) return 0;

            return totalScore / maxScore;
        }

        public List<float[]> Predict(List<double[]>[] input)
        {
            float[] flatInput = input.SelectMany(v => v.SelectMany(v => v.Select(v => (float)v))).ToArray();
            var modelInput = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_1", new DenseTensor<float>(flatInput, new int[] { input.Length, 32, 49 })),
            };

            var outputs = new float[input.Length, 8];

            lock (inferenceSessionLock)
            {
                using (var output = (inferenceSessionAccNew).Run(modelInput, new[] { "time_distributed_2" }))
                {
                    var flatOutput = (output.First().Value as IEnumerable<float>).ToArray();
                    System.Buffer.BlockCopy(flatOutput, 0, outputs, 0, outputs.Length * sizeof(float));
                }
            }

            var listOutputs = new List<float[]>();
            for (int i = 0; i < input.Length; ++i)
            {
                var output = new float[8];
                for (int j = 0; j < 8; ++j)
                {
                    output[j] = outputs[i, j];
                }
                listOutputs.Add(output);
            }
            return listOutputs;
        }

        public (List<float>, List<double>, int) PredictHitsForMap(DifficultyV3 mapdata, double bpm, double timescale = 1, double njsMult = 1)
        {
            var (segments, noteTimes, freePoints) = dataProcessing.PreprocessMap(mapdata, bpm, timescale, njsMult);
            if (segments.Count == 0 || noteTimes.Count == 0)
            {
                return (new List<float>(), new List<double>(), freePoints);
            }

            var predictionsArraysAcc = new List<float[]>();

            for (int i = 0; i < segments.Count; i += BatchSize)
            {
                var batch = segments.GetRange(i, Math.Min(BatchSize, segments.Count - i)).ToArray();
                if (batch.Length == 0)
                {
                    break;
                }
                var accPrediction = Predict(batch);
                predictionsArraysAcc.AddRange(accPrediction);
            }

            var accs = new List<float>();

            for (int i = 0; i < predictionsArraysAcc.Count; i++)
            {
                var batchPred = predictionsArraysAcc[i];
                var batchInp = segments[i];

                for (int j = 0; j < batchPred.Length; j++)
                {
                    var pred = batchPred[j];
                    var inp = batchInp.Skip(DataProcessing.preSegmentSize).Take(batchInp.Count - DataProcessing.preSegmentSize - DataProcessing.preSegmentSize).ToArray()[0];

                    if (inp.Sum() == 0.0)
                    {
                        continue;
                    }

                    accs.Add(Math.Max(0, pred));
                }
            }

            return (accs, noteTimes, freePoints);
        }

        public PredictionResult? PredictHitsForMapAllNotes(DifficultyV3 mapdata, double bpm, double timescale = 1)
        {
            var (accs, noteTimes, freePoints) = PredictHitsForMap(mapdata, bpm, timescale);
            double AIacc = GetMapAccForHits(accs, freePoints);
            double adjustedAIacc = ScaleFarmability(AIacc, accs.Count, ((noteTimes.Last() - noteTimes.First() + 4) / timescale) + 2);
            AIacc = adjustedAIacc;

            return new PredictionResult
            {
                Notes = accs.Select((acc, index) => new PredictedNote { Acc = acc, Time = (float)noteTimes[index] }).ToArray(),
                AIAcc = (float)AIacc,
            };
        }

        public Dictionary<string, object>? PredictHitsForMapNotes(DifficultyV3 mapdata, double bpm, double timescale = 1)
        {
            var (accs, noteTimes, freePoints) = PredictHitsForMap(mapdata, bpm, timescale);
            double AIacc = GetMapAccForHits(accs, freePoints);
            double adjustedAIacc = ScaleFarmability(AIacc, accs.Count, ((noteTimes.Last() - noteTimes.First() + 4) / timescale) + 2);
            AIacc = adjustedAIacc;

            var rows = accs.Select((acc, index) => new List<double> { acc, noteTimes[index] });


            var notes = new Dictionary<string, object>
            {
                ["columns"] = new[] { "acc", "note_time" },
                ["rows"] = rows,
                ["AIacc"] = AIacc,
            };

            return notes;
        }

        List<(double, double)> scaleCurve = new List<(double, double)>() {
        (0.0, 0.0),
         (0.6003503520303082, 0.10734016938160607),
         (0.8000625740167697, 0.1670550225942915),
         (0.9000130232242154, 0.22043326958200493),
         (0.9004830061775256, 0.22079472143247547),
         (0.9008578584881118, 0.22108433778130776),
         (0.9012317019288727, 0.22137435968435726),
         (0.9016975863665102, 0.2217374591070017),
         (0.9020691571275727, 0.22202839767296267),
         (0.9025321984028848, 0.22239264654902058),
         (0.902901492813263, 0.22268450759080094),
         (0.9032697743659337, 0.22297678050218772),
         (0.903728700925489, 0.2233427026069449),
         (0.9040947010422176, 0.22363590648868426),
         (0.9045507736831233, 0.22400299601642126),
         (0.9049144889963928, 0.22429713681826335),
         (0.9053677035885798, 0.22466540124182088),
         (0.9057291308582497, 0.22496048497073595),
         (0.9061794834320446, 0.225329931834937),
         (0.9065386195472332, 0.22562596455585027),
         (0.906986106295722, 0.22599660147842737),
         (0.9074319993347607, 0.22636790286330488),
         (0.9077875655580991, 0.22666542398750644),
         (0.9082305871790576, 0.2270379274277119),
         (0.9085838548982231, 0.22733641331166182),
         (0.9090240017209117, 0.22771012661557138),
         (0.9094625500357997, 0.22808451546802871),
         (0.9098122371154416, 0.2283845145887214),
         (0.9102479057151944, 0.2287601256033323),
         (0.910681973192462, 0.2291364190510281),
         (0.9110280737760723, 0.2294379468299907),
         (0.9114592570446779, 0.2298154749267708),
         (0.9118888368138129, 0.23019369244695265),
         (0.9123168122931197, 0.23057260191312606),
         (0.9126580370766751, 0.23087622939121183),
         (0.9130831229422995, 0.2312563907614374),
         (0.9135066024717686, 0.231637251215024),
         (0.913928474994476, 0.23201881332781527),
         (0.914348739871884, 0.23240107968991697),
         (0.9147673964977523, 0.23278405290580323),
         (0.9151844442983702, 0.23316773559442294),
         (0.9155169238200572, 0.23347519433492692),
         (0.9159310743941844, 0.23386016072083338),
         (0.916343614711684, 0.23424584399045378),
         (0.916754544324557, 0.23463224681872935),
         (0.9171638628184423, 0.23501937189560096),
         (0.9175715698128453, 0.23540722192612146),
         (0.9179776649613693, 0.2357957996305694),
         (0.9183821479519463, 0.23618510774456292),
         (0.9187850185070672, 0.2365751490191757),
         (0.9191862763840127, 0.23696592622105395),
         (0.9196656568111918, 0.23743583420593833),
         (0.9200633661146927, 0.23782824026388932),
         (0.9204594621997773, 0.23822139120839592),
         (0.920853944972351, 0.23861528987280595),
         (0.9212468143743018, 0.2390099391066638),
         (0.9216380703837297, 0.2394053417758345),
         (0.9221054479399644, 0.23988082357291024),
         (0.9224931545886673, 0.24027789396884247),
         (0.9228792480207663, 0.24067572708199075),
         (0.9233404308828069, 0.24115413773927785),
         (0.9237229757580672, 0.24155365918819754),
         (0.9241039079732349, 0.24195395280882098),
         (0.924558898279205, 0.24243532863220346),
         (0.924936283586876, 0.24283733158199783),
         (0.9253120571789561, 0.24324011633487347),
         (0.9257608585568515, 0.24372449430951199),
         (0.9261330877524391, 0.24412900977896695),
         (0.926577637134521, 0.24461547354801821),
         (0.926946324168112, 0.245021734671595),
         (0.9273866244870451, 0.24551030227615733),
         (0.9278246085523616, 0.2460000251153689),
         (0.9281878267087355, 0.2464090139018808),
         (0.9286215672428673, 0.24690086892216662),
         (0.9289812508322953, 0.24731164226993396),
         (0.9294107520707715, 0.24780564811889622),
         (0.9298379429380284, 0.24830083509456546),
         (0.9302628249351512, 0.24879720885843115),
         (0.9306151306883049, 0.24921176434865655),
         (0.9310357839415226, 0.2497103287307138),
         (0.9314541330468524, 0.250210096164748),
         (0.9318701798381535, 0.25071107247081265),
         (0.9322839262373341, 0.2512132635112973),
         (0.932695374254855, 0.25171667519133983),
         (0.9331045259902306, 0.25222131345924215),
         (0.9335113836325192, 0.25272718430689306),
         (0.9339159494608191, 0.2532342937701939),
         (0.9343182258447497, 0.2537426479294916),
         (0.9347182152449318, 0.2542522529100157),
         (0.9351159202134625, 0.2547631148823213),
         (0.9355113433943836, 0.25527524006273705),
         (0.9359697901686557, 0.25587432435208246),
         (0.9363602789878844, 0.2563892080297637),
         (0.9367484950041108, 0.2569053749077684),
         (0.9371985451695712, 0.2575091999783638),
         (0.937581847306741, 0.2580281691847736),
         (0.9379628865664157, 0.25854844206211497),
         (0.9384045767942713, 0.25915708354370215),
         (0.9387807247110888, 0.2596802036336343),
         (0.9392167179267369, 0.2602921850685731),
         (0.9396496517145889, 0.26090598016804445),
         (0.940018307281584, 0.2614335419524784),
         (0.940445575241413, 0.2620507339641663),
         (0.9408698013000969, 0.2626697707025034),
         (0.9412310074412941, 0.2632018502542468),
         (0.9416496015963305, 0.2638243423332387),
         (0.9420651728615702, 0.2644487110058738),
         (0.942477728244985, 0.26507496762090643),
         (0.9428872749392461, 0.2657031236303513),
         (0.9432938203223926, 0.2663331905907398),
         (0.9436973719584751, 0.26696518016439574),
         (0.9440979375981774, 0.2675991041207309),
         (0.9444955251794026, 0.26823497433755933),
         (0.9449462748165486, 0.26896408171560354),
         (0.9453375084475107, 0.2696041629994546),
         (0.9457257901926643, 0.2702462285948241),
         (0.9461659373982003, 0.2709824634526558),
         (0.9465479232779083, 0.2716288228500322),
         (0.9469808988941315, 0.27236999778592597),
         (0.9473566308160905, 0.2730207088014467),
         (0.9477824838094953, 0.27376689055838577),
         (0.9482045588345974, 0.27451577037906605),
         (0.9486228707579505, 0.2752673678457759),
         (0.9489858187078611, 0.2759272605053248),
         (0.9493971160253739, 0.2766840070860189),
         (0.9498046948070988, 0.2774435288146157),
         (0.9502085711897503, 0.27820584612015026),
         (0.9506585271099185, 0.2790668204043074),
         (0.9510545909720712, 0.27983514717960706),
         (0.9514470049779814, 0.2806063348472998),
         (0.9518841298062041, 0.281477367269969),
         (0.9522688463492481, 0.2822547056384368),
         (0.9526973572927545, 0.2831327129046036),
         (0.9530744584949815, 0.28391630087407405),
         (0.9534944497046729, 0.28480139558853956),
         (0.9539099697844494, 0.28569028900372434),
         (0.9543210466282215, 0.28658301386719387),
         (0.9547277086479302, 0.2874796033518093),
         (0.9551299847716085, 0.2883800910631242),
         (0.9555279044412088, 0.28928451104694164),
         (0.955921497610191, 0.2901928977970392),
         (0.956353786130413, 0.2912069113125735),
         (0.9567383462278782, 0.2921237876845015),
         (0.9571606754166833, 0.2931473224905135),
         (0.9575778262760373, 0.29417594067776554),
         (0.9579898435289135, 0.2952096929915714),
         (0.9583967726457088, 0.2962486309409123),
         (0.9587986598369218, 0.29729280681383824),
         (0.959195552045315, 0.29834227369325816),
         (0.9596264211809225, 0.2995028627201676),
         (0.9600129799333861, 0.3005636171263851),
         (0.9604326006146046, 0.30173675635642944),
         (0.9608464253791357, 0.3029165784443814),
         (0.9612545216244297, 0.30410315996526427),
         (0.9616569577292293, 0.30529657881783284),
         (0.9620538030343048, 0.30649691425526027),
         (0.9624804315118866, 0.3078143542258363),
         (0.9628658151550471, 0.30902941385896815),
         (0.9632801110282952, 0.3103631142649431),
         (0.9636881103349972, 0.3117054587504845),
         (0.9640899091651707, 0.31305656009627203),
         (0.9645183069814164, 0.3145302685311633),
         (0.9649075017817169, 0.31589998534744534),
         (0.9653224779095899, 0.3173941394097498),
         (0.9657306610292263, 0.31889915102882704),
         (0.9661321803554704, 0.3204151791574757),
         (0.9665572830594981, 0.3220603315075376),
         (0.9669453804567639, 0.3235997633667379),
         (0.9673563360182557, 0.3252705104053172),
         (0.9677602002707602, 0.3269548448445557),
         (0.9681852341976657, 0.3287748199947597),
         (0.9685749464453822, 0.33048801484182827),
         (0.9689852085123076, 0.33233944408293),
         (0.9693881387329771, 0.33420757314799165),
         (0.9698100882245698, 0.3362189937545381),
         (0.9702241944182711, 0.3382501405245402),
         (0.9706307156745811, 0.3403014042053906),
         (0.9710299106122553, 0.3423731872709509),
         (0.971446316968697, 0.34459740397805805),
         (0.9718550541467265, 0.3468457665431811),
         (0.9722564308833218, 0.3491188049749892),
         (0.9726737369463366, 0.35155305631696804),
         (0.9730834983375098, 0.3540162587921992),
         (0.9734860747468062, 0.35650910934340296),
         (0.9738818223293286, 0.3590323303865494),
         (0.9742925363298273, 0.3617295068189604),
         (0.9746964420022868, 0.36446227201260806),
         (0.9751146918106288, 0.367378361441084),
         (0.9755263016894108, 0.3703360958116499),
         (0.9759317205504938, 0.3733366818489754),
         (0.9763313860505544, 0.3763813794997551),
         (0.9767453081157705, 0.37962722829436657),
         (0.9771538329589436, 0.3829247568938137),
         (0.9775574204695061, 0.3862756376332149),
         (0.9779754120173848, 0.38984522080424994),
         (0.9783702476996005, 0.3933109178318859),
         (0.9787799759037069, 0.3970049436708181),
         (0.9791861352746015, 0.4007660540910947),
         (0.9796073882532946, 0.4047725472765047),
         (0.9800075050623989, 0.4086787582217678),
         (0.980423255679185, 0.4128428598436148),
         (0.9808188327090664, 0.41690581665674603),
         (0.9812305338401699, 0.4212404350391626),
         (0.9816406502548699, 0.42566771485407556),
         (0.9820494695680838, 0.430191704460561),
         (0.9824572497713367, 0.43481672346199657),
         (0.9828642204258444, 0.4395473874922171),
         (0.9832705841135677, 0.44438863589917466),
         (0.9836765181270832, 0.4493457627423146),
         (0.9840821763735244, 0.45442445159134615),
         (0.9845053211258176, 0.4598601744120041),
         (0.9849108076293412, 0.4652067915709929),
         (0.9853163652344854, 0.4706950980712935),
         (0.9857220759264133, 0.4763328085670776),
         (0.9861280099591841, 0.48212828545504993),
         (0.9865342282968508, 0.48809061348242394),
         (0.9869407850077634, 0.4942296854160182),
         (0.9873477295817633, 0.5005563008006252),
         (0.9877551091480568, 0.507082280282374),
         (0.988180715358901, 0.5141186011395664),
         (0.9885891314936663, 0.5210937416529832),
         (0.9889803371592701, 0.527992878980742),
         (0.989389952289551, 0.535460099280927),
         (0.9898002729013508, 0.5432066452513751),
         (0.9902113731352855, 0.5512542291320531),
         (0.9906233382268411, 0.5596271974591991),
         (0.9910362666808646, 0.5683529752428318),
         (0.9914322483611784, 0.5770580926654209),
         (0.991847409348529, 0.5865678864281907),
         (0.9922457886974023, 0.5960919424390858),
         (0.9926637759754662, 0.6065405821646188),
         (0.993065215406563, 0.6170536982964379),
         (0.9934868390015257, 0.6286469166315802),
         (0.9938922466963461, 0.6403783844945216),
         (0.9943000243543926, 0.6528144710471044),
         (0.994710519555982, 0.666045257568372),
         (0.9951052614592826, 0.6795154667361197),
         (0.9955222897068845, 0.6946348004319254),
         (0.9959241609946798, 0.7101754583858633),
         (0.9963302320906318, 0.7269774985228425),
         (0.9967409716074388, 0.7452639179514023),
         (0.9971567328192074, 0.7653226332347454),
         (0.9975573858065462, 0.7864209093867824),
         (0.9979619597970514, 0.8099147885553253),
         (0.9983683891380578, 0.8364188081105579),
         (0.9987916298728423, 0.8684630018343936),
         (0.999198672672967, 0.9063889338931731),
         (0.9995946244774474, 0.9578734354807452),
         (1, 1.0)
        };

        private double GetMultiplierForAccScale(double acc)
        {
            if (acc > 1)
                return 1;

            double previousCurvePointAcc = 0;
            double previousCurvePointMultiplier = 0;

            foreach (var (curvePointAcc, curvePointMultiplier) in scaleCurve)
            {
                if (acc <= curvePointAcc)
                {
                    double accDiff = curvePointAcc - previousCurvePointAcc;
                    double multiplierDiff = curvePointMultiplier - previousCurvePointMultiplier;
                    double slope = multiplierDiff / accDiff;
                    double multiplier = previousCurvePointMultiplier + slope * (acc - previousCurvePointAcc);
                    return multiplier;
                }

                previousCurvePointAcc = curvePointAcc;
                previousCurvePointMultiplier = curvePointMultiplier;
            }

            throw new InvalidOperationException("The accuracy value did not match any curve point.");
        }

        private double GetAccForMultiplierScale(double multiplier)
        {
            double previousCurvePointMultiplier = 0;
            double previousCurvePointAcc = 0;

            foreach (var (curvePointAcc, curvePointMultiplier) in scaleCurve)
            {
                if (multiplier <= curvePointMultiplier)
                {
                    double multDiff = curvePointMultiplier - previousCurvePointMultiplier;
                    double accDiff = curvePointAcc - previousCurvePointAcc;
                    double slope = accDiff / multDiff;
                    double acc = previousCurvePointAcc + slope * (multiplier - previousCurvePointMultiplier);
                    return acc;
                }

                previousCurvePointMultiplier = curvePointMultiplier;
                previousCurvePointAcc = curvePointAcc;
            }

            return 1.0;
        }

        public double ScaleFarmability(double acc, int noteCount, double mapLength, double farmSessionLength = 60 * 60)
        {
            const int baseAttemptsCount = 30;
            const double baseMultiplier = 0.030963633;
            const int baseNoteCount = 200;
            double noteScale = (double)noteCount / baseNoteCount;
            double attemptsScale = (farmSessionLength / mapLength) / baseAttemptsCount;
            double noteScaler = 1 / (1 + 5 * Math.Pow(noteScale, 0.69)) * 6.9;

            double attemptMultiplier = Math.Log(attemptsScale) + 2.7081502061025433;
            double attemptsScaler = Math.Pow(attemptMultiplier >= 0 ? attemptMultiplier : 0, 0.69) / 2.0;
            double multiplier = GetMultiplierForAccScale(acc) + noteScaler * attemptsScaler * baseMultiplier * (Math.Min(1, baseNoteCount * 10.0 / noteCount));
            return GetAccForMultiplierScale(multiplier);
        }

        public double GetAIAcc(DifficultyV3 mapdata, double bpm, double timescale, double njsMult = 1)
        {
            var (accs, noteTimes, freePoints) = PredictHitsForMap(mapdata, bpm, timescale, njsMult);
            double AIacc = GetMapAccForHits(accs, freePoints);
            double adjustedAIacc = ScaleFarmability(AIacc, accs.Count, ((noteTimes.Last() - noteTimes.First() + 4) / timescale) + 2);
            AIacc = adjustedAIacc;
            return AIacc;
        }

        // https://deepnote.com/workspace/beatleader-d4376e93-8e9f-461e-9143-e88974e31843/project/BeatLeader-38f67242-d369-4190-9d39-6f957aa93130/notebook/Map%20Categories-9defcb89bd864ca9ac11a55b0f7f9298
        public string Tag(float acc, float tech, float pass)
        {

            var input = new DenseTensor<float>(new float[] { acc, tech, pass }, new int[] { 1, 3 });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("float_input", input)
            };

            using var results = tagSession.Run(inputs);

            return results.First().AsTensor<string>().First();
        }
    }
}