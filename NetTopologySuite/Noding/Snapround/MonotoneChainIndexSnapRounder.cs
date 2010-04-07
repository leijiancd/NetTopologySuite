using System;
using System.Collections.Generic;
using GeoAPI.Coordinates;
using GeoAPI.Geometries;
using GisSharpBlog.NetTopologySuite.Algorithm;
using NPack.Interfaces;

namespace GisSharpBlog.NetTopologySuite.Noding.Snapround
{
    /// <summary>
    /// Uses Snap Rounding to compute a rounded,
    /// fully noded arrangement from a set of <see cref="NodedSegmentString{TCoordinate}"/>s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the Snap Rounding technique described in 
    /// Hobby, Guibas and Marimont, and Goodrich et al.
    /// Snap Rounding assumes that all vertices lie on a uniform grid
    /// (hence the precision model of the input must be fixed precision,
    /// and all the input vertices must be rounded to that precision).
    /// </para>
    /// <para>
    /// This implementation uses a monotone chains and a spatial index to
    /// speed up the intersection tests.
    /// This implementation appears to be fully robust using an integer precision model.
    /// It will function with non-integer precision models, but the
    /// results are not 100% guaranteed to be correctly noded.
    /// </para>
    /// </remarks>
    public class MonotoneChainIndexSnapRounder<TCoordinate> : INoder<TCoordinate>
        where TCoordinate : ICoordinate<TCoordinate>, IEquatable<TCoordinate>, IComparable<TCoordinate>,
            IComputable<Double, TCoordinate>, IConvertible
    {
        private readonly ICoordinateFactory<TCoordinate> _coordFactory;
        private readonly IGeometryFactory<TCoordinate> _geoFactory;
        private readonly LineIntersector<TCoordinate> _li;
        private readonly Double _scaleFactor;
        private IList<TCoordinate> _interiorIntersections;
        private IEnumerable<ISegmentString<TCoordinate>> _nodedSegStrings;
        private MonotoneChainIndexNoder<TCoordinate> _noder;
        private MonotoneChainIndexPointSnapper<TCoordinate> _pointSnapper;

        /// <summary>
        /// Initializes a new instance of the 
        /// <see cref="MonotoneChainIndexSnapRounder{TCoordinate}"/> class.
        /// </summary>
        /// <param name="geoFactory">The <see cref="IGeometryFactory{TCoordinate}" /> to use.</param>
        public MonotoneChainIndexSnapRounder(IGeometryFactory<TCoordinate> geoFactory)
            : this(geoFactory, geoFactory.PrecisionModel)
        {
        }

        /// <summary>
        /// Initializes a new instance of the 
        /// <see cref="MonotoneChainIndexSnapRounder{TCoordinate}"/> class.
        /// </summary>
        /// <param name="geoFactory">The <see cref="IGeometryFactory{TCoordinate}" /> to use.</param>
        public MonotoneChainIndexSnapRounder(IGeometryFactory<TCoordinate> geoFactory,
                                             IPrecisionModel<TCoordinate> precisionModel)
        {
            _geoFactory = geoFactory;
            _coordFactory = geoFactory.CoordinateFactory;
            _li = CGAlgorithms<TCoordinate>.CreateRobustLineIntersector(geoFactory);
            IPrecisionModel<TCoordinate> pm = precisionModel;
            _li.PrecisionModel = pm;
            _scaleFactor = pm.Scale;
        }

        #region INoder<TCoordinate> Members

        /// <summary>
        /// Computes the noding for a collection of <see cref="NodedSegmentString{TCoordinate}" />s
        /// and returns a set of fully noded <see cref="NodedSegmentString{TCoordinate}"/>s.
        /// The <see cref="NodedSegmentString{TCoordinate}"/>s have the same context as 
        /// their parent.
        /// </summary>
        /// <remarks>
        /// Some noders may add all these nodes to the input <see cref="NodedSegmentString{TCoordinate}" />s;
        /// others may only add some or none at all.
        /// </remarks>
        public IEnumerable<ISegmentString<TCoordinate>> Node(
            IEnumerable<ISegmentString<TCoordinate>> inputSegmentStrings)
        {
            //_nodedSegStrings = inputSegmentStrings;
            //IntersectionFinderAdder<TCoordinate> intFinderAdder = new IntersectionFinderAdder<TCoordinate>(_li);
            //_noder = new MonotoneChainIndexNoder<TCoordinate>(_geoFactory, intFinderAdder);
            //_pointSnapper = new MonotoneChainIndexPointSnapper<TCoordinate>(_geoFactory, _noder.MonotoneChains,
            //                                                                 _noder.Index);
            //_interiorIntersections = intFinderAdder.InteriorIntersections;
            //snapRound(inputSegmentStrings, _li);
            ComputeNodes(inputSegmentStrings);
            return NodedSegmentString<TCoordinate>.GetNodedSubstrings(_nodedSegStrings);
        }

        public void ComputeNodes(
            IEnumerable<ISegmentString<TCoordinate>> inputSegmentStrings)
        {
            _nodedSegStrings = inputSegmentStrings;
            IntersectionFinderAdder<TCoordinate> intFinderAdder = new IntersectionFinderAdder<TCoordinate>(_li);
            _noder = new MonotoneChainIndexNoder<TCoordinate>(_geoFactory, intFinderAdder);
            _pointSnapper = new MonotoneChainIndexPointSnapper<TCoordinate>(_geoFactory, _noder.MonotoneChains,
                                                                             _noder.Index);
            _interiorIntersections = intFinderAdder.InteriorIntersections;
            snapRound(inputSegmentStrings, _li);
        }

        #endregion

        private void snapRound(IEnumerable<ISegmentString<TCoordinate>> segStrings, LineIntersector<TCoordinate> li)
        {
            //_noder.ComputeNodes(segStrings);
            segStrings = _noder.Node(segStrings);
            IEnumerable<TCoordinate> intersections = _interiorIntersections;
            //_noder.Node(segStrings,  Func<NodedSegmentString<TCoordinate>,TCoordinate>( ) );
            computeIntersectionSnaps(intersections);
            computeVertexSnaps(segStrings);
        }

        /// <summary>
        /// Computes nodes introduced as a result of snapping segments to snap points (hot pixels).
        /// </summary>
        private void computeIntersectionSnaps(IEnumerable<TCoordinate> snapPts)
        {
            foreach (TCoordinate snapPt in snapPts)
            {
                HotPixel<TCoordinate> hotPixel = new HotPixel<TCoordinate>(
                    snapPt, _scaleFactor, _li, _coordFactory);

                _pointSnapper.Snap(hotPixel);
            }
        }

        /// <summary>
        /// Computes nodes introduced as a result of
        /// snapping segments to vertices of other segments.
        /// </summary>
        private void computeVertexSnaps(IEnumerable<ISegmentString<TCoordinate>> edges)
        {
            foreach (NodedSegmentString<TCoordinate> edge in edges)
            {
                computeVertexSnaps(edge);
            }
        }

        /// <summary>
        /// Performs a brute-force comparison of every segment in each 
        /// <see cref="NodedSegmentString{TCoordinate}" />.
        /// This has O(n^2) performance.
        /// </summary>
        private void computeVertexSnaps(NodedSegmentString<TCoordinate> e)
        {
            IEnumerable<TCoordinate> coordinates = e.Coordinates;

            Int32 index = 0;

            foreach (TCoordinate coordinate in coordinates)
            {
                HotPixel<TCoordinate> hotPixel = new HotPixel<TCoordinate>(
                    coordinate, _scaleFactor, _li, _coordFactory);
                Boolean isNodeAdded = _pointSnapper.Snap(hotPixel, e, index);

                // if a node is created for a vertex, that vertex must be noded too
                if (isNodeAdded)
                {
                    e.AddIntersection(coordinate, index);
                }
            }
        }

        //private void checkCorrectness(IEnumerable<SegmentString<TCoordinate>> inputSegmentStrings)
        //{
        //    IEnumerable<SegmentString<TCoordinate>> resultSegStrings 
        //        = SegmentString<TCoordinate>.GetNodedSubstrings(inputSegmentStrings);

        //    NodingValidator<TCoordinate> nv = new NodingValidator<TCoordinate>(resultSegStrings);

        //    try
        //    {
        //        nv.CheckValid();
        //    }
        //    catch (Exception ex)
        //    {
        //        Trace.WriteLine(ex.ToString());
        //    }
        //}
    }
}